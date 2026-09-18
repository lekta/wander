using System.IO;

namespace Wander.App.Util;

/// <summary>
/// "Somebody holds this file", as .NET on Windows reports it: an
/// <see cref="IOException"/> carrying ERROR_SHARING_VIOLATION. The platform
/// layer throws the same for a recycle that failed for that reason, so one
/// question covers a delete, a recycle and a read.
/// </summary>
public static class FileInUse {
    private const int SharingViolation = unchecked((int)0x80070020);


    public static bool Is(Exception? ex) {
        return ex is IOException { HResult: SharingViolation };
    }
}
