using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Wander.App.Util;
using Wander.App.ViewModels;
using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Shell;

namespace Wander.App.DragPreview;

/// <summary>
/// A drop that passed every check: what to drop, where, and how.
/// </summary>
public readonly record struct DropPlan(IReadOnlyList<string> Paths, string Target, DropEffect Effect);


/// <summary>
/// The receiving half of drag &amp; drop: given a position over the window,
/// works out which folder is under the cursor, whether dropping there is
/// allowed, what it would do (copy / move / shortcut), and draws the
/// highlight around the target.
///
/// <para>
/// One controller for every surface that accepts a drop — the file list in
/// all three modes, the drives tree, folder bookmarks — because the answer
/// has to be the same in all of them. The window keeps what is about the
/// <i>dragging</i> (the preview plaque that follows the cursor, the
/// bookmarks strip) and reads the properties here to describe what would
/// happen.
/// </para>
///
/// <para>
/// The controller decides but never acts: <see cref="PlanDrop"/> answers
/// with a plan and the caller runs it through the view model, so the file
/// operation keeps going through the one path that logs it, guards it and
/// makes it undoable.
/// </para>
/// </summary>
public sealed class DropTargetController {
    private readonly Func<string?> _currentFolder;

    private DropTargetAdorner? _adorner;
    private AdornerLayer? _adornerLayer;


    /// <param name="currentFolder">
    /// The folder being listed — the fallback target when the cursor is over
    /// the list's empty space rather than over a row.
    /// </param>
    public DropTargetController(Func<string?> currentFolder) {
        _currentFolder = currentFolder;
    }


    /// <summary>Folder the drop would go into, or null when there is none.</summary>
    public string? Target { get; private set; }

    /// <summary>What the drop would do, as WPF's own vocabulary.</summary>
    public DragDropEffects Effect { get; private set; }

    /// <summary>Why the drop is refused, when it is refused for a reason worth showing.</summary>
    public SelfDropReason SelfDropReason { get; private set; }

    /// <summary>The dragged item that caused <see cref="SelfDropReason"/>.</summary>
    public string? SelfDropOffender { get; private set; }

    /// <summary>
    /// The target is the folder being listed rather than something the
    /// cursor is actually pointing at — empty space in the list, or a row
    /// that is a file. The drop still works (that is what makes "drop into
    /// this window" a thing), but the user has not aimed at anything yet, so
    /// refusing out loud here would be shouting at someone who has only just
    /// picked the file up.
    /// </summary>
    public bool TargetIsFallback { get; private set; }

    /// <summary>
    /// The cursor is over the bookmarks strip, where a drop adds a bookmark
    /// rather than moving anything. Set by the window's own strip handlers
    /// and cleared here the moment the cursor is back over a real folder —
    /// without that, the plaque kept offering "переместить … в Downloads"
    /// while hovering the strip.
    /// </summary>
    public bool IsBookmarkTarget { get; set; }


    /// <summary>
    /// Answers a <c>DragOver</c>: fills in <c>e.Effects</c>, remembers what
    /// would happen so the plaque can describe it, and moves the highlight.
    /// </summary>
    public void DragOver(DragEventArgs e) {
        // Reaching the ordinary pipeline means the cursor is over a real
        // drop target again, whatever it was over a moment ago.
        IsBookmarkTarget = false;
        e.Handled = true;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effects = DragDropEffects.None;
            Reset();
            SetHighlight(null);

            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        string? target = ResolveTarget(e, out bool aimed);
        if (target is null) {
            e.Effects = DragDropEffects.None;
            Reset();
            SetHighlight(null);

            return;
        }

        // Self-drop checks don't apply to Link — Explorer happily makes a
        // shortcut next to the original.
        var reason = IsLinkGesture()
            ? SelfDropReason.None
            : PathSafety.DetectSelfDrop(paths, target, out _);

        if (reason != SelfDropReason.None) {
            PathSafety.DetectSelfDrop(paths, target, out string? offender);
            e.Effects = DragDropEffects.None;
            Effect = DragDropEffects.None;
            Target = target;
            SelfDropReason = reason;
            SelfDropOffender = offender;
            TargetIsFallback = !aimed;
            SetHighlight(null);

            return;
        }

        e.Effects = ChooseEffect(paths, target);
        Effect = e.Effects;
        Target = target;
        SelfDropReason = SelfDropReason.None;
        SelfDropOffender = null;
        TargetIsFallback = !aimed;
        SetHighlight(FindHighlightElement(e));
    }


    /// <summary>
    /// Answers a <c>Drop</c>. Null means the drop is refused — no file list
    /// in the payload, nowhere to put it, or a folder dropped into itself.
    /// The checks are repeated rather than taken from the last
    /// <see cref="DragOver"/>: the modifier keys can change between the last
    /// move and the release, and that is exactly how a move becomes a copy.
    /// </summary>
    public DropPlan? PlanDrop(DragEventArgs e) {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) {
            return null;
        }

        var paths = ((string[])e.Data.GetData(DataFormats.FileDrop)).ToList();
        string? target = ResolveTarget(e, out _);
        if (target is null) {
            return null;
        }

        if (!IsLinkGesture() && PathSafety.DetectSelfDrop(paths, target, out _) != SelfDropReason.None) {
            return null;
        }

        var effect = ChooseEffect(paths, target) switch {
            DragDropEffects.Move => DropEffect.Move,
            DragDropEffects.Link => DropEffect.Link,
            _ => DropEffect.Copy,
        };

        return new DropPlan(paths, target, effect);
    }


    /// <summary>
    /// Answers a <c>Drop</c> end to end: works out the plan, hands it to
    /// <paramref name="run"/>, marks the event handled and takes the
    /// highlight down — including when the drop was refused or
    /// <paramref name="run"/> threw.
    ///
    /// <para>
    /// Still deciding rather than acting: what a plan <em>does</em> is the
    /// caller's, and every surface passes the same thing — the view model,
    /// the one path that logs a file operation, guards it and makes it
    /// undoable.
    /// </para>
    /// </summary>
    public void Execute(DragEventArgs e, Action<DropPlan> run) {
        try {
            if (PlanDrop(e) is not { } plan) {
                return;
            }

            run(plan);
            e.Handled = true;
        } finally {
            Clear();
        }
    }


    /// <summary>Takes the highlight down and forgets the last target.</summary>
    public void Clear() {
        SetHighlight(null);
        Reset();
    }


    /// <summary>
    /// True when the cursor is over a bookmark row that is a real on-disk
    /// folder — the case where a drop copies into it, instead of adding a
    /// bookmark. Shell-namespace bookmarks (the Recycle Bin) have no backing
    /// directory and answer false.
    /// </summary>
    public static bool IsOverDroppableBookmarkFolder(DragEventArgs e) {
        foreach (var element in Ancestors(e)) {
            if (element.DataContext is not TreeNodeViewModel node) {
                continue;
            }

            return !string.IsNullOrEmpty(node.FullPath)
                && !node.FullPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }


    /// <summary>
    /// Which folder is under the cursor. A row that is a folder is that
    /// folder; a shortcut to one is what it points at; a tree node is its
    /// path. Anything else — empty space in the list, the space between
    /// tiles — means the folder being listed, which is what makes dropping
    /// "into this window" work at all.
    /// </summary>
    /// <param name="aimed">
    /// True when the target is the thing under the cursor, false when it is
    /// the fallback — see <see cref="TargetIsFallback"/>.
    /// </param>
    private string? ResolveTarget(DragEventArgs e, out bool aimed) {
        aimed = true;
        foreach (var element in Ancestors(e)) {
            if (element.DataContext is FileSystemEntry entry) {
                if (entry.Kind == EntryKind.Directory) {
                    return entry.FullPath;
                }

                if (entry.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    && ResolveShortcutTarget(entry.FullPath) is { } resolved
                    && Directory.Exists(resolved)) {
                    return resolved;
                }
            }

            if (element.DataContext is TreeNodeViewModel node && !string.IsNullOrEmpty(node.FullPath)) {
                return node.FullPath;
            }
        }

        aimed = false;

        return _currentFolder();
    }

    private static string? ResolveShortcutTarget(string lnkPath) {
        try {
            return ServiceLocator.Get<IShortcutService>().Resolve(lnkPath);
        } catch {
            return null;
        }
    }


    /// <summary>
    /// The element to draw the highlight around: the row under the cursor
    /// when it is a folder, nothing otherwise.
    /// </summary>
    private static UIElement? FindHighlightElement(DragEventArgs e) {
        foreach (var hit in ListVisuals.Ancestors(e.OriginalSource)) {
            switch (hit) {
                case TreeViewItem tvi when tvi.DataContext is TreeNodeViewModel:
                    // RenderSize of a TreeViewItem includes its expanded
                    // children — adorning that would paint the highlight over
                    // the whole subtree. The default WPF template names the
                    // row container "Bd" (Aero2); adorn that if available,
                    // otherwise fall back to the row itself.
                    return tvi.Template?.FindName("Bd", tvi) as UIElement ?? tvi;

                case ListBoxItem lbi when lbi.DataContext is FileSystemEntry { Kind: EntryKind.Directory }:
                    return lbi;

                case DataGridRow dgr when dgr.DataContext is FileSystemEntry { Kind: EntryKind.Directory }:
                    return dgr;
            }
        }

        return null;
    }

    /// <summary>Moves the highlight; null takes it down.</summary>
    public void SetHighlight(UIElement? target) {
        if (_adorner is not null && _adornerLayer is not null) {
            _adornerLayer.Remove(_adorner);
            _adorner = null;
            _adornerLayer = null;
        }

        if (target is null || AdornerLayer.GetAdornerLayer(target) is not { } layer) {
            return;
        }

        _adorner = new DropTargetAdorner(target);
        _adornerLayer = layer;
        layer.Add(_adorner);
    }


    private void Reset() {
        IsBookmarkTarget = false;
        Effect = DragDropEffects.None;
        Target = null;
        SelfDropReason = SelfDropReason.None;
        SelfDropOffender = null;
        TargetIsFallback = false;
    }


    /// <summary>
    /// Which operation the modifiers ask for, defaulting to what Explorer
    /// does without any: move within a drive, copy across drives.
    /// </summary>
    private static DragDropEffects ChooseEffect(IReadOnlyList<string> paths, string target) {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Alt)) {
            return DragDropEffects.Link;
        }
        if (mods.HasFlag(ModifierKeys.Shift)) {
            return DragDropEffects.Move;
        }
        if (mods.HasFlag(ModifierKeys.Control)) {
            return DragDropEffects.Copy;
        }

        return paths.Count > 0 && IsSameDrive(paths[0], target)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    private static bool IsLinkGesture() {
        return (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
    }

    private static bool IsSameDrive(string a, string b) {
        return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// The element under the cursor and everything above it — the walk both
    /// "what is this" questions above are built on. Goes through
    /// <see cref="ListVisuals.Ancestors"/> because the cursor can be over a
    /// <c>Run</c>, which is not a visual and cannot be asked for its visual
    /// parent.
    /// </summary>
    private static IEnumerable<FrameworkElement> Ancestors(DragEventArgs e) {
        foreach (var hit in ListVisuals.Ancestors(e.OriginalSource)) {
            if (hit is FrameworkElement fe) {
                yield return fe;
            }
        }
    }
}
