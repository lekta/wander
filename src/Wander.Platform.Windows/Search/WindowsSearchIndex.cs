using System.Text;
using Wander.Core.Logging;
using Wander.Core.Search;

namespace Wander.Platform.Windows.Search;

/// <summary>
/// The "somewhere on this machine" scope, answered by the catalogue
/// Windows Search already keeps.
///
/// <para>
/// Reached through the <c>Search.CollatorDSO</c> OLE DB provider, driven by
/// late-bound <c>ADODB</c> rather than <c>System.Data.OleDb</c>. That is a
/// deliberate trade: the typed route is one NuGet package, the late-bound
/// route is one <c>ProgID</c> and a handful of <c>dynamic</c> calls, and
/// the shape of the work — open, execute, walk a recordset of one column —
/// is small enough that the types were not buying much.
/// </para>
///
/// <para>
/// Everything this returns is what the index believes, which is not the
/// same as what is on the disk: the index covers the folders Windows was
/// told to cover, and reads document contents only where a filter is
/// installed. The caller says so in as many words rather than presenting
/// the answer as exhaustive.
/// </para>
/// </summary>
public sealed class WindowsSearchIndex : IIndexedSearch {
    private const string ConnectionString =
        "Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\";";

    private readonly ILogger _log;
    private readonly object _gate = new();
    private bool? _available;


    public WindowsSearchIndex(ILogger? log = null) {
        _log = log ?? NullLogger.Instance;
    }


    /// <summary>
    /// Whether the provider is there and answering. Probed once with a
    /// trivial query and remembered: the indexing service can be disabled
    /// outright, and the failure is a COM exception rather than an empty
    /// result, which is not something to discover per keystroke.
    /// </summary>
    public bool IsAvailable {
        get {
            lock (_gate) {
                _available ??= Probe();

                return _available.Value;
            }
        }
    }


    public IReadOnlyList<string> Search(
        string query,
        string? scopePath,
        bool searchContents,
        int limit,
        CancellationToken token) {
        if (string.IsNullOrWhiteSpace(query) || !IsAvailable) {
            return Array.Empty<string>();
        }

        // Contents first: it is the query the user asked for, and it is the
        // one that can be rejected outright when what they typed is nothing
        // but noise words ("the", "и"). The name-only form always parses,
        // so it is the fallback rather than a second try at the same thing.
        var paths = Query(BuildSql(query, scopePath, searchContents, limit), token);
        if (paths is null && searchContents) {
            paths = Query(BuildSql(query, scopePath, false, limit), token);
        }

        return paths ?? (IReadOnlyList<string>)Array.Empty<string>();
    }


    /// <summary>
    /// Runs one statement, or null when the provider refused it. Null and
    /// empty are kept apart on purpose — "the index has nothing" and "the
    /// index would not answer" lead to different next steps.
    /// </summary>
    private IReadOnlyList<string>? Query(string sql, CancellationToken token) {
        object? connection = CreateConnection();
        if (connection is null) {
            return null;
        }

        dynamic conn = connection;
        try {
            conn.Open(ConnectionString);
        } catch (Exception ex) {
            _log.Warn($"Windows Search: cannot open the catalogue ({ex.Message})");

            return null;
        }

        try {
            dynamic records = conn.Execute(sql);
            var paths = new List<string>();

            while (!records.EOF) {
                token.ThrowIfCancellationRequested();
                if (ToPath(records.Fields.Item(0).Value as string) is { } path) {
                    paths.Add(path);
                }
                records.MoveNext();
            }

            return paths;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _log.Warn($"Windows Search: query refused ({ex.Message})");

            return null;
        } finally {
            try {
                conn.Close();
            } catch (Exception ex) {
                _log.Warn($"Windows Search: close failed ({ex.Message})");
            }
        }
    }


    private bool Probe() {
        if (CreateConnection() is not { } connection) {
            return false;
        }

        dynamic conn = connection;
        try {
            conn.Open(ConnectionString);
            conn.Close();
            _log.Info("Windows Search index available");

            return true;
        } catch (Exception ex) {
            _log.Info($"Windows Search index unavailable: {ex.Message}");

            return false;
        }
    }


    private object? CreateConnection() {
        try {
            var type = Type.GetTypeFromProgID("ADODB.Connection");

            return type is null ? null : Activator.CreateInstance(type);
        } catch (Exception ex) {
            _log.Warn($"Windows Search: ADODB unavailable ({ex.Message})");

            return null;
        }
    }


    /// <summary>
    /// The query in the SQL dialect the index speaks.
    /// <c>System.ItemUrl</c> rather than <c>System.ItemPathDisplay</c>:
    /// the display path is localised, so on a Russian Windows it hands back
    /// <c>C:\Пользователи\…</c> — a path that reads correctly and opens
    /// nothing.
    /// </summary>
    private static string BuildSql(string query, string? scopePath, bool searchContents, int limit) {
        var sql = new StringBuilder();
        sql.Append("SELECT TOP ").Append(limit).Append(" System.ItemUrl FROM SystemIndex WHERE ");

        if (!string.IsNullOrEmpty(scopePath)) {
            sql.Append("SCOPE='file:").Append(Literal(scopePath.Replace('\\', '/'))).Append("' AND ");
        }

        sql.Append("(System.FileName LIKE '%").Append(Literal(EscapeLike(query))).Append("%'");
        if (searchContents) {
            sql.Append(" OR CONTAINS(System.Search.Contents,'\"")
               .Append(Literal(query.Replace("\"", "\"\"")))
               .Append("\"')");
        }
        sql.Append(')');

        return sql.ToString();
    }


    /// <summary>A SQL string literal's insides: the quote is doubled.</summary>
    private static string Literal(string value) {
        return value.Replace("'", "''");
    }


    /// <summary>
    /// Neutralises the wildcards of <c>LIKE</c>. Without this, a search for
    /// "50%" matches every file on the machine — and the user typing it is
    /// looking for a file about a discount, not writing a pattern.
    /// </summary>
    private static string EscapeLike(string value) {
        var escaped = new StringBuilder(value.Length);
        foreach (char c in value) {
            if (c is '%' or '_' or '[') {
                escaped.Append('[').Append(c).Append(']');
            } else {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }


    /// <summary>
    /// An <c>ItemUrl</c> as a filesystem path, or null for the rows that
    /// are not files at all — the index also holds mail, browser history
    /// and anything else a provider registered.
    ///
    /// <para>
    /// Local files arrive as <c>file:C:/dir/name</c> and network ones as
    /// <c>file://server/share/name</c>; the two leading slashes of the
    /// second are the UNC prefix and have to survive, not be trimmed off
    /// with the rest.
    /// </para>
    /// </summary>
    private static string? ToPath(string? url) {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string rest = url["file:".Length..];
        string path = rest.StartsWith("//", StringComparison.Ordinal)
            ? @"\\" + rest[2..].Replace('/', '\\')
            : rest.TrimStart('/').Replace('/', '\\');

        return path.Length > 2 ? path : null;
    }
}
