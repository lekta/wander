using Wander.Core.FileSystem;
using Wander.Core.Logging;

namespace Wander.Core.Search;

/// <summary>
/// Runs one search and streams what it finds.
///
/// <para>
/// The shape is a walk, not an index — the measurements behind that are in
/// <c>REJECTED.md</c>. Directories are walked one at a time
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
    private readonly ILogger _log;


    /// <param name="extractors">
    /// Tried in order; the first one that returns text wins. Order matters:
    /// the format-specific ones go first and <see cref="PlainTextExtractor"/>
    /// — which is willing to try anything — goes last.
    /// </param>
    public ContentSearchService(
        IFileSystem fs,
        IReadOnlyList<IContentExtractor> extractors,
        ExtractedTextCache cache,
        ILogger? log = null) {
        _fs = fs;
        _extractors = extractors;
        _cache = cache;
        _log = log ?? NullLogger.Instance;
    }


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
        // "Everything, anywhere" is not a search, it is a mistake with a
        // five-thousand-row result. The callers all guard against it; this
        // is the guard that does not depend on them remembering.
        if (request.IsEmpty) {
            return new SearchOutcome(0, 0, false, 0);
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

                // A folder has no contents, so a search that asks for
                // text cannot be satisfied by one. Asking for a name only
                // — it can.
                if (!request.HasText && request.Name.Matches(entry.Name)) {
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
            $"Search name '{request.Name.Text}' text '{request.Text}' in {request.Root} " +
            $"({request.Scope}, binaries: {request.SearchBinaries}): " +
            $"{found} hits, {counters.Scanned} files scanned, {counters.Unreadable} unreadable" +
            (truncated ? ", truncated" : ""));

        return new SearchOutcome(counters.Scanned, found, truncated, counters.Unreadable);
    }


    /// <summary>
    /// The files of one folder, tested in parallel. The name mask is
    /// settled first because it is free, and because it is a gate rather
    /// than an alternative: a file the mask rejects is never opened, which
    /// is what makes "every .cs that mentions X" cost a fraction of "every
    /// file that mentions X".
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

            // The mask is a gate. Rejected here, the file is never opened
            // and never counted as scanned — it was not a candidate.
            if (!request.Name.Matches(entry.Name)) {
                return;
            }

            counters.CountScanned();

            if (!request.HasText) {
                slots[i] = new SearchHit(entry, null, 0);

                return;
            }

            // A file too large to open cannot answer a question about its
            // contents, so it is out. Passing the mask is not enough when
            // the mask is only half of what was asked.
            if (entry.Size > request.MaxFileSize) {
                return;
            }

            string? text = ExtractText(entry, request, token, out bool knownFormat);
            if (text is null) {
                if (request.SearchBinaries && MatchesRawBytes(entry, request, token)) {
                    // No snippet: a binary has no lines, and a window of
                    // mojibake around the hit would be a lie dressed as
                    // context.
                    slots[i] = new SearchHit(entry, null, 0);
                } else if (knownFormat) {
                    counters.CountUnreadable();
                }

                return;
            }

            if (ContentMatcher.TryMatch(text, request.Text, out string snippet, out int line)) {
                // The snippet rides on the entry as well as on the hit: the
                // list binds to entries, and copying it across here saves
                // every consumer from carrying a second lookup table.
                slots[i] = new SearchHit(entry with { MatchSnippet = snippet }, snippet, line);
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
                // A format-specific extractor that claimed the file and
                // failed ends the search for this file. Falling through to
                // the catch-all would let it decide a PDF is "text" on the
                // strength of the ASCII in its header — and then match a
                // query against `%PDF-1.4 ReportLab Generated PDF`, which
                // is a hit on a document nobody could actually read.
                if (knownFormat) {
                    return null;
                }

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
    /// Byte-for-byte scan of a file that is not text, for the opt-in
    /// binaries mode. A second read of a file the extractors already
    /// opened, which is the price of keeping the extractor contract to one
    /// question ("what does this say") instead of two.
    /// </summary>
    private bool MatchesRawBytes(FileSystemEntry entry, SearchRequest request, CancellationToken token) {
        if (!BinaryTextSearch.Supports(request.Text)) {
            return false;
        }

        try {
            byte[] bytes = _fs.ReadAllBytes(entry.FullPath);
            token.ThrowIfCancellationRequested();

            return BinaryTextSearch.Contains(bytes, request.Text);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) {
            return false;
        }
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
