using System.Security.Cryptography;
using System.Text;
using Wander.Core.Logging;

namespace Wander.Platform.Windows.Persistence;

/// <summary>
/// Who owns <c>state.json</c> under a data root. The instance the user
/// runs holds a named mutex for the root; an instance started with
/// <c>--yield</c> (the Rider launch profile) holds nothing and asks
/// before every write whether the owner is alive - and once it has seen
/// the owner it never writes again this session, so whatever it saved
/// earlier cannot land on top of what the owner wrote in between.
///
/// <para>
/// The name carries the root, so a harness in its sandbox and the installed
/// copy on the real data never see each other. Nothing is ever waited on:
/// the mutex is a flag with a lifetime, and existence is the whole
/// question. Two instances neither of which yields - two windows of the
/// installed copy - both hold it and both write, exactly as before.
/// </para>
/// </summary>
public sealed class InstanceLock : IDisposable {
    private readonly string _name;
    private readonly ILogger _log;
    private readonly Mutex? _held;
    private bool _yielded;


    public InstanceLock(string dataRoot, bool yields, ILogger log) {
        _name = NameFor(dataRoot);
        _log = log;
        Yields = yields;
        if (!yields) {
            // Created but never acquired: holding the handle is what keeps
            // the object alive for others to find, and it goes with the
            // process, crash included.
            _held = new Mutex(false, _name);
        }
    }


    /// <summary>Started with <c>--yield</c>: this instance never owns the file.</summary>
    public bool Yields { get; }

    /// <summary>
    /// True when a write must be skipped: this instance yields and the
    /// owner has been seen running at some point in this session.
    /// </summary>
    public bool IsYielding {
        get {
            if (!Yields || _yielded) {
                return _yielded;
            }

            if (Mutex.TryOpenExisting(_name, out var owner)) {
                owner.Dispose();
                _yielded = true;
                _log.Info("state.json: another instance owns it under this data root; this one yields and will not write it");
            }

            return _yielded;
        }
    }


    public void Dispose() {
        _held?.Dispose();
    }


    /// <summary>
    /// A kernel object name for the root: session-local, and a hash rather
    /// than the path itself, because backslashes are the one character a
    /// mutex name may not contain.
    /// </summary>
    private static string NameFor(string dataRoot) {
        string normalised = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));

        return "Local\\Wander.state." + Convert.ToHexString(hash, 0, 8);
    }
}
