using System.Windows.Media;
using Wander.Core.FileSystem;


namespace Wander.App.ViewModels;

/// <summary>
/// One colour swatch in the preview footer's rating row. Five of these are
/// created once per <see cref="Controllers.PreviewController"/> and stay put; only
/// <see cref="IsSelected"/> moves as the selection changes, so the row never
/// rebuilds itself under the cursor.
///
/// <para>
/// The index and the name come from <see cref="ColorLabels"/> in Core — the
/// brush is the only part that belongs to the view layer, because a colour
/// meaning is shared between formats but a <see cref="Brush"/> is WPF.
/// </para>
/// </summary>
public sealed class ColorLabelViewModel : ObservableObject {
    private bool _isSelected;


    /// <summary>
    /// Adobe's colour-label palette, in the index order both sidecar
    /// formats use. A factory rather than a shared array because the
    /// swatches carry <see cref="IsSelected"/>: the preview footer and the
    /// gallery's filter bar mean different things by "chosen", and one set
    /// of five would have them fighting over it.
    /// </summary>
    public static IReadOnlyList<ColorLabelViewModel> CreateChoices() {
        return new[] {
            new ColorLabelViewModel(1, new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F))),
            new ColorLabelViewModel(2, new SolidColorBrush(Color.FromRgb(0xE0, 0xB3, 0x2C))),
            new ColorLabelViewModel(3, new SolidColorBrush(Color.FromRgb(0x5C, 0xA9, 0x4D))),
            new ColorLabelViewModel(4, new SolidColorBrush(Color.FromRgb(0x3E, 0x7C, 0xC4))),
            new ColorLabelViewModel(5, new SolidColorBrush(Color.FromRgb(0x8A, 0x5C, 0xB8))),
        };
    }


    public ColorLabelViewModel(int index, Brush brush) {
        Index = index;
        Brush = brush;
        Name = ColorLabels.Name(index);
    }


    /// <summary>1…5. Index 0 ("none") has no swatch — clicking the selected one clears instead.</summary>
    public int Index { get; }

    public Brush Brush { get; }

    public string Name { get; }

    public bool IsSelected {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
