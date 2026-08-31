using System.Runtime.InteropServices;
using Wander.Core.FileSystem;

namespace Wander.Platform.Windows.FileSystem;

/// <summary>
/// Resolves KNOWNFOLDERIDs via <c>SHGetKnownFolderPath</c>. Used for the
/// "Downloads" default bookmark — the BCL's <c>Environment.SpecialFolder</c>
/// has no Downloads entry, and falling back to <c>%USERPROFILE%\Downloads</c>
/// silently breaks on localised installs or when the user moved the folder.
/// </summary>
public sealed class WindowsKnownFolders : IKnownFolders {
    // FOLDERID_Downloads — {374DE290-123F-4565-9164-39C4925E467B}
    private static readonly Guid _downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    // FOLDERID_Documents — {FDD39AD0-238F-46AF-ADB4-6C85480369C7}
    private static readonly Guid _documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    // FOLDERID_Pictures — {33E28130-4E1E-4676-835A-98395C3BC3BB}
    private static readonly Guid _pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    // FOLDERID_Desktop — {B4BFCC3A-DB2C-424C-B029-7FE99A87C641}
    private static readonly Guid _desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    // FOLDERID_Music — {4BD8D571-6D19-48D3-BE97-422220080E43}
    private static readonly Guid _music = new("4BD8D571-6D19-48D3-BE97-422220080E43");
    // FOLDERID_Videos — {18989B1D-99B5-455B-841C-AB7C74E4DDFC}
    private static readonly Guid _videos = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");


    public string? GetDownloads() {
        return TryGetPath(_downloads);
    }

    public string? GetDocuments() {
        return TryGetPath(_documents);
    }

    public string? GetPictures() {
        return TryGetPath(_pictures);
    }

    public string? GetDesktop() {
        return TryGetPath(_desktop);
    }

    public string? GetMusic() {
        return TryGetPath(_music);
    }

    public string? GetVideos() {
        return TryGetPath(_videos);
    }


    private static string? TryGetPath(Guid folderId) {
        IntPtr p = IntPtr.Zero;
        try {
            int hr = SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out p);
            if (hr != 0 || p == IntPtr.Zero) {
                return null;
            }
            return Marshal.PtrToStringUni(p);
        } catch {
            return null;
        } finally {
            if (p != IntPtr.Zero) {
                Marshal.FreeCoTaskMem(p);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}
