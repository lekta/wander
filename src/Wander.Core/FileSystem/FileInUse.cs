namespace Wander.Core.FileSystem;

/// <summary>
/// "Somebody holds this file", as .NET on Windows reports it: an
/// <see cref="IOException"/> carrying ERROR_SHARING_VIOLATION. The platform
/// layer throws the same for a recycle that failed for that reason, so one
/// question covers a delete, a recycle, a move and a read - and the wait for
/// a held path (<see cref="BusyGate"/>) knows what to wait out.
/// </summary>
public static class FileInUse {
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int AccessDenied = unchecked((int)0x80070005);


    public static bool Is(Exception? ex) {
        return ex is IOException { HResult: SharingViolation };
    }

    /// <summary>
    /// "Access denied" on an <see cref="IOException"/> - what Windows answers
    /// a move or a rename of a folder while a file inside it is held (stand
    /// 2026-10-19: <c>Directory.Move</c> gives 0x80070005, not a sharing
    /// violation, whatever the holder shares). Held only when a held file is
    /// found inside (<see cref="BusyGate.Retry"/>); otherwise the folder is
    /// really closed to us.
    /// </summary>
    public static bool MayBeHeldInside(Exception? ex) {
        return ex is IOException { HResult: AccessDenied };
    }

    /// <summary>The failure <see cref="Is"/> recognises, for a path given up on without being tried again.</summary>
    public static IOException Error(string path) {
        return new IOException($"'{path}' or something inside it is in use", SharingViolation);
    }
}
