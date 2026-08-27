using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Core.Search;

/// <summary>
/// Runs one search and streams what it finds.
///
/// <para>
/// The shape is a walk, not an index — see <see cref="IIndexedSearch"/>
/// for the measurements behind that. Directories are walked one at a time
/// so results arrive in a sensible order and memory stays flat; the files
/// inside each are read in parallel, because that is the part that waits
/// on the disk.
/// </para>
///
/// <para>
/// Hits are handed back a directory at a time rather than one at a time.
/// A search over a source tree finds hundreds of files in a second, and
/// marshalling each one onto the UI thread on its own is how a list ends
/// up redrawing itself five hundred times — the same lesson
/// <c>BulkObservableCollection</c> records for folder listings.
/// </para>
/// </summary>
public sealed class ContentSearchService {
    /// <summary>
    /// Files read at once. Capped rather than left to
    /// <see cref="Environment.ProcessorCount"/>: past a handful the disk is
    /// the limit, and on a spinning drive or a network share more readers
    /// make it slower, not faster.
    /// </summary>
    private const int MaxReaders = 8;

    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<IContentExtractor> _extractors;
    private readonly ExtractedTextCache _cache;
    private readonly IIndexedSearch? _index;
    private readonly ILogger _log;


    /// <param name="extractors">
    /// Tried in order; the first one that returns text wins. Order matters:
    /// the format-specific ones go first and <see cref="PlainTextExtractor"/>
    /// — which is willing to try anything — goes last.
    /// </param>
    /// <param name="index">The system index, when there is one. Null disables <see cref="SearchScope.Computer"/>.</param>
    public ContentSearchService(
        IFileSystem fs,
        IReadOnlyList<IContentExtractor> extractors,
        ExtractedTextCache cache,
        IIndexedSearch? index = null,
        ILogger? log = null) {
        _fs = fs;
        _extractors = extractors;
        _cache = cache;
        _index = index;
        _log = log ?? NullLogger.Instance;
    }


    /// <summary>True when <see cref="SearchScope.Computer"/> can be offered.</summary>
    public bool CanSearchComputer => _index is { IsAvailable: true };


    /// <summary>
    /// Walks the request's scope and reports what matches.
    /// <paramref name="onBatch"/> is called from a background thread, once
    /// per folder that yielded anything; marshalling is the caller's job.
    /// </summary>
    public Task<SearchOutcome> RunAsync(
        SearchRequest request,
        Action<IReadOnlyList<SearchHit>> onBatch,
        IProgress<SearchProgress>? progress,
        CancellationToken token) {
        return Task.Run(() => Run(request, onBatch, progress, token), token);
    }


    private SearchOutcome Run(
        SearchRequest request,
        Action<IReadOnlyList<SearchHit>> onBatch,
        IProgress<SearchProgress>? progress,
        CancellationToken token) {
        if (request.Scope == SearchScope.Computer) {
            return RunIndexed(request, onBatch, token);
        }

        var counters = new PassCounters();
        int found = 0;
        bool truncated = false;

        var pending = new Queue<Step>();
        pending.Enqueue(new Step(request.Root, 0));

        // A junction pointing at one of its own ancestors is a loop, and
        // the depth cap alone would walk it sixty-four levels deep before
        // noticing. Remembering where we have been ends it at the first
        // repeat.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { request.Root };

        while (pending.Count > 0 && !truncated) {
            token.ThrowIfCancellationRequested();
            var step = pending.Dequeue();

            IReadOnlyList<FileSystemEntry> entries;
            try {
                entries = _fs.Enumerate(step.Folder, SortOptions.Default);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                // A folder we may not read is not an error the user asked
                // about — it is one of dozens on any real disk.
                continue;
            }

            var files = new List<FileSystemEntry>();
            var hits = new List<SearchHit>();

            foreach (var entry in entries) {
                if (!request.Visibility.Allows(entry)) {
                    continue;
                }

                if (entry.Kind != EntryKind.Directory) {
                    files.Add(entry);
                    continue;
                }

                // A folder has no contents to search, only a name.
                if (entry.Name.Contains(request.Query, StringComparison.OrdinalIgnoreCase)) {
                    hits.Add(new SearchHit(entry, null, 0));
                }
                if (request.Scope == SearchScope.Subfolders
                    && step.Depth < request.MaxDepth
                    && visited.Add(entry.FullPath)) {
                    pending.Enqueue(new Step(entry.FullPath, step.Depth + 1));
                }
            }

            hits.AddRange(MatchFiles(files, request, counters, token));

            // The cap is applied here rather than inside the parallel pass:
            // the readers are already running, and stopping them mid-folder
            // would drop hits that come alphabetically before ones we keep.
            if (found + hits.Count >= request.MaxResults) {
                int room = request.MaxResults - found;
                if (hits.Count > room) {
                    hits.RemoveRange(room, hits.Count - room);
                }
                truncated = true;
            }

            found += hits.Count;
            if (hits.Count > 0) {
                onBatch(hits);
            }
            progress?.Report(new SearchProgress(counters.Scanned, found, step.Folder));
        }

        _log.Info(
            $"Search '{request.Query}' in {request.Root} ({request.Scope}, contents: {request.SearchContents}): " +
            $"{found} hits, {counters.Scanned} files scanned, {counters.Unreadable} unreadable" +
            (truncated ? ", truncated" : ""));

        return new SearchOutcome(counters.Scanned, found, truncated, counters.Unreadable);
    }


    /// <summary>
    /// The files of one folder, tested in parallel. The name match is free
    /// and is settled first; only what the name did not already answer
    /// costs a read.
    /// </summary>
    private List<SearchHit> MatchFiles(
        List<FileSystemEntry> files,
        SearchRequest request,
        PassCounters counters,
        CancellationToken token) {
        // Results land in a slot each rather than in a shared list, so the
        // order of the folder's listing survives the parallel pass — a
        // result list that reshuffles itself between two identical searches
        // is one nobody can navigate.
        var slots = new SearchHit?[files.Count];

        var options = new ParallelOptions {
            MaxDegreeOfParallelism = Math.Min(MaxReaders, Environment.ProcessorCount),
            CancellationToken = token,
        };

        Parallel.For(0, files.Count, options, i => {
            var entry = files[i];
            counters.CountScanned();

            bool nameMatches = entry.Name.Contains(request.Query, StringComparison.OrdinalIgnoreCase);

            if (!request.SearchContents) {
                if (nameMatches) {
                    slots[i] = new SearchHit(entry, null, 0);
                }

                return;
            }

            string? text = ExtractText(entry, request, token, out bool knownFormat);
            if (text is null) {
                if (nameMatches) {
                    slots[i] = new SearchHit(entry, null, 0);
                } else if (knownFormat) {
                    counters.CountUnreadable();
                }

                return;
            }

            if (ContentMatcher.TryMatch(text, request.Query, out string snippet, out int line)) {
                // The snippet rides on the entry as well as on the hit: the
                // list binds to entries, and copying it across here saves
                // every consumer from carrying a second lookup table.
                slots[i] = new SearchHit(entry with { MatchSnippet = snippet }, snippet, line);
            } else if (nameMatches) {
                slots[i] = new SearchHit(entry, null, 0);
            }
        });

        var hits = new List<SearchHit>();
        foreach (var slot in slots) {
            if (slot is not null) {
                hits.Add(slot);
            }
        }

        return hits;
    }


    /// <summary>
    /// A file's text, from the cache when an expensive extractor has
    /// already paid for it. Null when nothing could read the file, and for
    /// files past <see cref="SearchRequest.MaxFileSize"/> — those are still
    /// matched by name, they are just never opened.
    /// </summary>
    /// <param name="knownFormat">
    /// True when a format-specific extractor claimed the file. This is what
    /// separates "a document we should have been able to read" from "a
    /// <c>.dll</c>", and only the first is worth reporting as unreadable:
    /// a search through a source tree passes thousands of binaries, and
    /// counting those would turn the warning into noise that hides the one
    /// case it exists for — a folder of PDFs on a machine with no PDF
    /// filter installed.
    /// </param>
    private string? ExtractText(
        FileSystemEntry entry,
        SearchRequest request,
        CancellationToken token,
        out bool knownFormat) {
        knownFormat = false;
        if (entry.Size > request.MaxFileSize) {
            return null;
        }

        long size = entry.Size ?? 0;
        string? cached = _cache.Get(entry.FullPath, size, entry.ModifiedUtc);
        if (cached is not null) {
            return cached;
        }

        foreach (var extractor in _extractors) {
            token.ThrowIfCancellationRequested();
            if (!extractor.CanExtract(entry.FullPath)) {
                continue;
            }
            // "Expensive" and "format-specific" are the same set: the
            // catch-all is the cheap one, and it is willing to try anything.
            knownFormat |= extractor.IsExpensive;

            string? text;
            try {
                text = extractor.Extract(entry.FullPath, token);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                // Extractors promise to answer null rather than throw. One
                // that breaks the promise must still not end a search over
                // ten thousand other files.
                _log.Warn($"Extractor {extractor.GetType().Name} failed on {entry.FullPath}: {ex.Message}");
                continue;
            }

            if (text is null) {
                continue;
            }

            if (extractor.IsExpensive) {
                _cache.Put(entry.FullPath, size, entry.ModifiedUtc, text);
            }

            return text;
        }

        return null;
    }


    /// <summary>
    /// The whole-machine scope. The index returns paths; turning each back
    /// into an entry is one stat per result, which is affordable for a few
    /// thousand and gives the list the size, date and kind it needs to draw
    /// a row.
    /// </summary>
    private SearchOutcome RunIndexed(
        SearchRequest request,
        Action<IReadOnlyList<SearchHit>> onBatch,
        CancellationToken token) {
        if (_index is not { IsAvailable: true }) {
            return new SearchOutcome(0, 0, false, 0);
        }

        IReadOnlyList<string> paths;
        try {
            paths = _index.Search(request.Query, null, request.SearchContents, request.MaxResults, token);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _log.Error("Indexed search failed", ex);

            return new SearchOutcome(0, 0, false, 0);
        }

        var hits = new List<SearchHit>(paths.Count);
        foreach (string path in paths) {
            token.ThrowIfCancellationRequested();

            FileSystemEntry? entry;
            try {
                entry = _fs.GetEntry(path);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                continue;
            }

            // The index outlives the files in it: anything deleted since
            // the last crawl is dropped rather than shown as a row that
            // opens nothing.
            if (entry is null || !request.Visibility.Allows(entry)) {
                continue;
            }
            hits.Add(new SearchHit(entry, null, 0));
        }

        if (hits.Count > 0) {
            onBatch(hits);
        }
        _log.Info($"Search '{request.Query}' via system index: {hits.Count} hits of {paths.Count} returned");

        return new SearchOutcome(hits.Count, hits.Count, paths.Count >= request.MaxResults, 0);
    }


    private readonly record struct Step(string Folder, int Depth);


    /// <summary>
    /// The two counters the parallel readers share. A class rather than
    /// locals because <see cref="MatchFiles"/> raises them from several
    /// threads at once.
    /// </summary>
    private sealed class PassCounters {
        private int _scanned;
        private int _unreadable;


        public int Scanned => Volatile.Read(ref _scanned);

        public int Unreadable => Volatile.Read(ref _unreadable);


        public void CountScanned() {
            Interlocked.Increment(ref _scanned);
        }


        public void CountUnreadable() {
            Interlocked.Increment(ref _unreadable);
        }
    }
}
