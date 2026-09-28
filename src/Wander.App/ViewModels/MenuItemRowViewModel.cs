using System.Windows;
using Wander.Core.Menu;

namespace Wander.App.ViewModels;

/// <summary>
/// One line of the "Основные пункты" table in settings: an entry Wander
/// draws itself, standard or its own.
///
/// <para>
/// A table rather than a column of checkboxes, to match the third-party one
/// right above it — and because the flat list was quietly lying about the
/// menu. "Вставить" lives inside "Файл"; showing the two as siblings makes
/// the reader wonder what unticking "Файл" does to the rows under it. Here
/// the child rows are indented, so the answer is visible.
/// </para>
/// </summary>
public sealed class MenuItemRowViewModel : ObservableObject {
    /// <summary>Indent per level. One tab stop, enough to read as "inside".</summary>
    private const double IndentStep = 22;

    private readonly Action _onChanged;
    private bool _isHidden;


    public MenuItemRowViewModel(MenuNode node, bool isHidden, Action onChanged) {
        Key = node.Id.ToString();
        Title = ContextMenuCatalog.Title(node.Id);
        Gesture = ContextMenuCatalog.Gesture(node.Id) ?? string.Empty;
        Indent = new Thickness(node.Depth * IndentStep, 0, 0, 0);
        IsSubmenu = node.Id is MenuCommandId.OpenSubmenu or MenuCommandId.FileSubmenu or MenuCommandId.NewSubmenu
            or MenuCommandId.ActionsSubmenu or MenuCommandId.ConvertSubmenu or MenuCommandId.ToFolderSubmenu;
        _isHidden = isHidden;
        _onChanged = onChanged;
    }


    /// <summary>The <see cref="MenuCommandId"/> name, which is what gets persisted.</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>The hotkey the entry advertises, or empty. Still works when the row is hidden.</summary>
    public string Gesture { get; }

    /// <summary>Left padding that puts a submenu's children under their header.</summary>
    public Thickness Indent { get; }

    /// <summary>A submenu header — drawn in semibold so the grouping reads at a glance.</summary>
    public bool IsSubmenu { get; }

    /// <summary>
    /// Ticked = the entry is in the menu - the table's "Вкл", the same way
    /// round as every checkbox of the settings (2026-09-28). Stored the
    /// other way round, as the hidden ones.
    /// </summary>
    public bool IsShown {
        get => !_isHidden;
        set {
            if (SetField(ref _isHidden, !value)) {
                _onChanged();
            }
        }
    }

    public bool IsHidden => _isHidden;
}
