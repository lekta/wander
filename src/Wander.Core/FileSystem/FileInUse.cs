namespace Wander.Core.FileSystem;

/// <summary>
/// "Somebody holds this file", as .NET on Windows reports it: an
/// <see cref="IOException"/> carrying ERROR_SHARING_VIOLATION. The platform
/// layer throws the same for a recycle that failed for that reason, so one
/// question covers a delete, a recycle, a move and a read - and the wait for
/// a held path (<see cref="Operations.BusyGate"/>) knows what to wait out.
/// </summary>
public static class FileInUse {
    private const int SharingViolation = unchecked((int)0x80070020);


    public static bool Is(Exception? ex) {
        return ex is IOException { HResult: SharingViolation };
    }

    /// <summary>The failure <see cref="Is"/> recognises, for a path given up on without being tried again.</summary>
    public static IOException Error(string path) {
        return new IOException($"'{path}' or something inside it is in use", SharingViolation);
    }
}
