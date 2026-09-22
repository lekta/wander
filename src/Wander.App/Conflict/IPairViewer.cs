using System.Windows;
using Wander.Core.FileSystem;

namespace Wander.App.Conflict;

/// <summary>
/// Opens two files side by side (PLAN Q5) - the compare window, which lives
/// with the views. A seam rather than a call: the views already depend on
/// this folder through the dialogs, and a call back up would be a cycle
/// between the two. Registered by <c>App.OnStartup</c>.
/// </summary>
public interface IPairViewer {
    void Show(FileSystemEntry left, FileSystemEntry right, Window owner);
}
