using System.Windows;
using System.Windows.Controls;
using Wander.App.Util;
using Wander.Core.FileSystem;
using Wander.Core.Listing;

namespace Wander.App.Controls;

/// <summary>
/// The table's "Размер" cell: a file's size, or what a folder holds, asked of
/// <see cref="FolderSizes"/> as the cell comes on screen. A control of its own
/// rather than a value on the row: the answer lands in this text and nowhere
/// else, so a folder's number coming in rebuilds no row and moves nothing.
/// </summary>
public sealed class EntrySizeText : TextBlock {
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(FileSystemEntry), typeof(EntrySizeText),
        new PropertyMetadata(null, (d, _) => ((EntrySizeText)d).Show()));


    public EntrySizeText() {
        Loaded += (_, _) => {
            if (Sizes is { } sizes) {
                sizes.Measured -= OnMeasured;
                sizes.Invalidated -= Show;
                sizes.Measured += OnMeasured;
                sizes.Invalidated += Show;
            }
            Show();
        };
        Unloaded += (_, _) => {
            if (Sizes is { } sizes) {
                sizes.Measured -= OnMeasured;
                sizes.Invalidated -= Show;
            }
        };
    }


    /// <summary>Who weighs the folders. A cell inside a row template has no DataContext to read it from; set once, before the window.</summary>
    public static FolderSizes? Sizes { get; set; }

    public FileSystemEntry? Entry {
        get => (FileSystemEntry?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }


    private void Show() {
        switch (Entry) {
            case { Size: { } bytes }:
                Text = SizeFormatter.Format(bytes);
                break;

            case { Kind: EntryKind.Directory } folder when Sizes is { } sizes:
                Text = sizes.Known(folder.FullPath) is { } held ? SizeFormatter.Format(held) : "";
                sizes.Want(folder.FullPath);
                break;

            default:
                Text = "";
                break;
        }
    }

    private void OnMeasured(string path, long size) {
        if (Entry is { Size: null } entry && string.Equals(entry.FullPath, path, StringComparison.OrdinalIgnoreCase)) {
            Text = SizeFormatter.Format(size);
        }
    }
}
