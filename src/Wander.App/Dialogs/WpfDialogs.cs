using System.Windows;
using Wander.App.Conflict;
using Wander.Core.FileSystem;

namespace Wander.App.Dialogs;

/// <summary>
/// The real thing: message boxes, the prompt dialog, the folder picker and
/// the modal conflict dialogs, all owned by whichever window is active so
/// they land on top of it.
/// </summary>
public sealed class WpfDialogs : IDialogs {
    public bool Ask(DialogRequest request) {
        var buttons = request.Buttons switch {
            DialogButtons.Ok => MessageBoxButton.OK,
            DialogButtons.YesNo => MessageBoxButton.YesNo,
            _ => MessageBoxButton.OKCancel,
        };
        var icon = request.Icon switch {
            DialogIcon.Information => MessageBoxImage.Information,
            DialogIcon.Question => MessageBoxImage.Question,
            DialogIcon.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Warning,
        };
        var defaultResult = request.Buttons switch {
            DialogButtons.Ok => MessageBoxResult.OK,
            DialogButtons.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };

        var owner = ActiveWindow();
        var result = owner is null
            ? MessageBox.Show(request.Message, request.Title, buttons, icon, defaultResult)
            : MessageBox.Show(owner, request.Message, request.Title, buttons, icon, defaultResult);

        return result is MessageBoxResult.OK or MessageBoxResult.Yes;
    }

    public string? Prompt(string title, string label, string initial, bool filenameMode) {
        return PromptDialog.Show(title, label, initial, filenameMode);
    }

    public string? PickFolder(string title, string? startAt = null) {
        var picker = new Microsoft.Win32.OpenFolderDialog {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(startAt)) {
            picker.InitialDirectory = startAt;
        }

        return picker.ShowDialog() == true ? picker.FolderName : null;
    }

    public IConflictResolver CreateConflictResolver(bool skipIdentical) {
        return new DispatcherConflictResolver(new InteractiveConflictResolver(skipIdentical));
    }


    private static Window? ActiveWindow() {
        var app = Application.Current;
        if (app is null) {
            return null;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
    }
}
