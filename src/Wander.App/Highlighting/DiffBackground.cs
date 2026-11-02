using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Wander.App.Resources;
using Wander.Core.Preview;

namespace Wander.App.Highlighting;

/// <summary>
/// A patch read as "before / after" rather than as code (decision of
/// 2026-11-02): each row painted across the width by what it is - added,
/// removed, a block's start, a file's header - and the text left in the
/// plain colour. What a row is, is <see cref="UnifiedDiff.Classify"/>'s;
/// this only paints. The brushes are looked up on every draw, so a switch
/// of the theme comes in with the editor's next redraw.
/// </summary>
public sealed class DiffBackground : IBackgroundRenderer {
    private DiffLineKind[] _kinds = Array.Empty<DiffLineKind>();


    public KnownLayer Layer => KnownLayer.Background;


    /// <summary>
    /// Paints <paramref name="editor"/>'s text as a diff, or stops. Called
    /// after the text is set: the rows are read once, here.
    /// </summary>
    public static void Show(TextEditor editor, bool on) {
        var view = editor.TextArea.TextView;
        var mine = view.BackgroundRenderers.OfType<DiffBackground>().FirstOrDefault();
        if (!on) {
            if (mine is not null) {
                view.BackgroundRenderers.Remove(mine);
            }

            return;
        }

        if (mine is null) {
            mine = new DiffBackground();
            view.BackgroundRenderers.Add(mine);
        }
        var document = editor.Document;
        mine._kinds = UnifiedDiff.Classify(document.Lines.Select(document.GetText).ToArray());
        view.InvalidateLayer(KnownLayer.Background);
    }

    public void Draw(TextView textView, DrawingContext drawingContext) {
        if (!textView.VisualLinesValid) {
            return;
        }

        foreach (var line in textView.VisualLines) {
            int index = line.FirstDocumentLine.LineNumber - 1;
            if (index >= _kinds.Length || BrushOf(_kinds[index]) is not { } brush) {
                continue;
            }

            double top = line.VisualTop - textView.ScrollOffset.Y;
            drawingContext.DrawRectangle(brush, null, new Rect(0, top, textView.ActualWidth, line.Height));
        }
    }


    private static Brush? BrushOf(DiffLineKind kind) {
        return kind switch {
            DiffLineKind.Added => Palette.DiffAddedLine.Current,
            DiffLineKind.Removed => Palette.DiffRemovedLine.Current,
            DiffLineKind.Hunk => Palette.DiffHunkLine.Current,
            DiffLineKind.FileHeader => Palette.DiffHeaderLine.Current,
            _ => null,
        };
    }
}
