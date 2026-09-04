using Wander.Core.FileSystem;

namespace Wander.Core.Tests.Fakes;

/// <summary>
/// Stands in for the OS clipboard: holds one payload, and can be told to
/// fail the way the real one does when another process has it open.
/// <see cref="Content"/> is settable so a test can play "the user copied
/// something in Explorer" without going through <see cref="SetFiles"/>.
/// </summary>
internal sealed class FakeSystemClipboard : ISystemClipboard {
    public List<string> CallLog { get; } = new();

    /// <summary>What the clipboard holds. Null models "holds nothing at all".</summary>
    public ClipboardFiles? Content { get; set; } = ClipboardFiles.Empty;

    /// <summary>When true, every call fails — the busy-clipboard case.</summary>
    public bool Fails { get; set; }

    /// <summary>The object handed to <see cref="SetShellObject"/>, if any.</summary>
    public object? SharedObject { get; private set; }


    public string? LastError { get; private set; }


    public bool SetFiles(IReadOnlyList<string> paths, bool isCut) {
        CallLog.Add($"Set:{(isCut ? "cut" : "copy")}:{string.Join(";", paths)}");
        if (Fails) {
            LastError = "busy";
            return false;
        }

        LastError = null;
        Content = new ClipboardFiles(paths.ToList(), isCut);

        return true;
    }

    /// <summary>
    /// A shell data object went out instead of a file list. The fake keeps
    /// the object itself and models what the real clipboard then reads
    /// back: no paths, and "there are files here that are not on disk".
    /// </summary>
    public bool SetShellObject(object dataObject) {
        CallLog.Add("SetShellObject");
        if (Fails) {
            LastError = "busy";
            return false;
        }

        LastError = null;
        SharedObject = dataObject;
        Content = new ClipboardFiles(Array.Empty<string>(), false, HasUnsupportedFiles: true);

        return true;
    }

    public ClipboardFiles? GetFiles() {
        CallLog.Add("Get");
        if (Fails) {
            LastError = "busy";
            return null;
        }

        LastError = null;

        return Content;
    }

    public void Clear() {
        CallLog.Add("Clear");
        if (Fails) {
            LastError = "busy";
            return;
        }

        LastError = null;
        Content = ClipboardFiles.Empty;
    }
}
