using System.ComponentModel;
using System.Windows.Data;
using Wander.App.Resources;

namespace Wander.App.ViewModels;

/// <summary>
/// One section in the settings dialog. The dialog selects the right-pane
/// DataTemplate by the concrete subclass type, so adding a new category
/// is just:
///   1. Create a new <c>FooSettingsCategory : SettingsCategoryViewModel</c>.
///   2. Add a <c>DataTemplate DataType="{x:Type vm:FooSettingsCategory}"</c>
///      in SettingsWindow.xaml.
///   3. Add an instance to <see cref="SettingsViewModel.Categories"/>.
/// No central switch / registry / reflection needed. Where a setting goes
/// and what it is called - ARCHITECTURE, "Настройки".
/// </summary>
public abstract class SettingsCategoryViewModel : ObservableObject {
    public string Title { get; }

    /// <summary>
    /// Back-pointer to the live settings VM. XAML reads/writes setting
    /// values via <c>{Binding Owner.PropName}</c> so the per-category
    /// VMs stay thin and don't have to forward every property by hand.
    /// </summary>
    public SettingsViewModel Owner { get; }

    /// <summary>
    /// The first page of a run in the list on the left (2026-09-25): what is
    /// on screen, what is done with files, the machinery - a little air
    /// above it says so without a heading.
    /// </summary>
    public bool StartsCluster { get; }

    /// <summary>
    /// A page about a part of the page above it, indented under it in the
    /// list (2026-09-28): the view's sizes and the gallery under "Вид", the
    /// menu, the actions, their programs and the ratings under "Файловые
    /// операции".
    /// </summary>
    public bool IsNested { get; }

    /// <summary>
    /// Where F1 opens the guide on the site (2026-09-28): the page, and its
    /// section after a #, that tells what this page's settings do - what
    /// goes beyond a label is written there, not in hints on the page. The
    /// site step of check.bat fails on a page or a section the built site
    /// does not have; it finds them by the named argument <c>guide</c> of
    /// each page's constructor.
    /// </summary>
    public string GuidePage { get; }

    protected SettingsCategoryViewModel(
        string title, SettingsViewModel owner, string guide, bool startsCluster = false, bool nested = false) {

        Title = title;
        Owner = owner;
        GuidePage = guide;
        StartsCluster = startsCluster;
        IsNested = nested;
    }
}


/// <summary>Where a session starts, the folder panel and the standard bookmarks.</summary>
public sealed class FoldersSettingsCategory : SettingsCategoryViewModel {
    public FoldersSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryFolders, owner, guide: "derevo-i-zakladki") { }
}


/// <summary>What the list shows and when it lists again.</summary>
public sealed class ListSettingsCategory : SettingsCategoryViewModel {
    public ListSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryList, owner, guide: "oblast-faylov") { }
}


/// <summary>Which view a folder gets: the default one, and the gallery for a folder of photos.</summary>
public sealed class ViewsSettingsCategory : SettingsCategoryViewModel {
    public ViewsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryViews, owner, guide: "vidy-i-sortirovka") { }
}


/// <summary>Every view's own sizes, each beside a preview drawn with them.</summary>
public sealed class SizesSettingsCategory : SettingsCategoryViewModel {
    public SizesSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategorySizes, owner, guide: "vidy-i-sortirovka#razmery", nested: true) { }
}


/// <summary>The gallery's background: three colours, two of them with a brightness of their own.</summary>
public sealed class GallerySettingsCategory : SettingsCategoryViewModel {
    public GallerySettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryGallery, owner, guide: "galereya", nested: true) { }
}


/// <summary>What is asked before a delete or a move, and about files that turn out the same.</summary>
public sealed class OperationsSettingsCategory : SettingsCategoryViewModel {
    public OperationsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryOperations, owner, guide: "rabota-s-faylami", startsCluster: true) { }
}


/// <summary>
/// Other programs' rows and Wander's own. The filter over the first table
/// lives here rather than on the owner, like the keyboard page's search: a
/// way of looking through a list, not a setting.
/// </summary>
public sealed class ContextMenuSettingsCategory : SettingsCategoryViewModel {
    public ContextMenuSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryContextMenu, owner, guide: "kontekstnoe-menyu#nastroyka", nested: true) {
        ShellRows = new ListCollectionView(owner.ShellExtensionRows) { Filter = Passes };
    }


    private string _query = string.Empty;
    /// <summary>What the filter field holds. Not persisted - see <see cref="HotkeysSettingsCategory.Query"/>.</summary>
    public string Query {
        get => _query;
        set {
            if (SetField(ref _query, value)) {
                ShellRows.Refresh();
            }
        }
    }

    /// <summary>
    /// The table: the owner's rows that pass the filter, in the order a click
    /// on a header chose. A view rather than a second list - the owner
    /// rebuilds its rows in place, and the view keeps the filter and the
    /// sort across that.
    /// </summary>
    public ICollectionView ShellRows { get; }


    private bool Passes(object item) {
        return item is ShellExtensionRowViewModel row && row.Matches(_query);
    }
}


/// <summary>
/// The actions catalog: presets and the user's own rows in one table, the
/// selected row's command in a form under it. The selected row lives here
/// rather than on the owner - a click in the table is not a setting, and the
/// owner's every property change is saved.
/// </summary>
public sealed class ActionsSettingsCategory : SettingsCategoryViewModel {
    public ActionsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryActions, owner, guide: "svoi-komandy", nested: true) {
        // The owner builds its rows anew on a reset of all settings, with
        // this page off screen and no table to drop the selection: a row
        // no longer in the table would stay on the card, and what is typed
        // into it would go nowhere.
        owner.ActionRows.CollectionChanged += (_, _) => {
            if (_selectedRow is not null && !owner.ActionRows.Contains(_selectedRow)) {
                SelectedRow = null;
            }
        };
    }


    private ActionRowViewModel? _selectedRow;
    public ActionRowViewModel? SelectedRow {
        get => _selectedRow;
        set {
            if (SetField(ref _selectedRow, value)) {
                Raise(nameof(HasSelection));
                Raise(nameof(CanRemove));
            }
        }
    }

    public bool HasSelection => _selectedRow is not null;

    /// <summary>A shipped row is switched off, not deleted.</summary>
    public bool CanRemove => _selectedRow is { IsPreset: false };


    public void Add() {
        SelectedRow = Owner.AddAction();
    }

    public void CopySelected() {
        if (_selectedRow is { } row) {
            SelectedRow = Owner.CopyAction(row);
        }
    }

    public void RemoveSelected() {
        if (_selectedRow is { IsPreset: false } row) {
            SelectedRow = null;
            Owner.RemoveAction(row);
        }
    }
}


/// <summary>The programs the presets need: found by themselves or pointed at by hand, one block each.</summary>
public sealed class ToolsSettingsCategory : SettingsCategoryViewModel {
    public ToolsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryTools, owner, guide: "konvertatsiya#programmy", nested: true) { }
}


/// <summary>
/// The file a photo's stars and label are written to (2026-09-28: off the
/// gallery's page - they are set in every view, through the preview pane).
/// </summary>
public sealed class RatingsSettingsCategory : SettingsCategoryViewModel {
    public RatingsSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryRatings, owner, guide: "otsenki-i-metki", nested: true) { }
}


/// <summary>What Wander keeps on disk and in memory: thumbnails, pictures, scratch copies.</summary>
public sealed class CacheSettingsCategory : SettingsCategoryViewModel {
    public CacheSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryCache, owner, guide: "dannye-i-kesh#kesh-miniatyur", startsCluster: true) { }
}


/// <summary>
/// The keyboard reference. Reads rather than edits — see
/// <see cref="HotkeyCatalog"/> for why the two are different tasks.
/// </summary>
public sealed class HotkeysSettingsCategory : SettingsCategoryViewModel {
    public HotkeysSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryHotkeys, owner, guide: "goryachie-klavishi") { }


    private string _query = string.Empty;
    /// <summary>
    /// What the search field holds. Not persisted: it is a way of looking
    /// through a list, not a preference, and a dialog that reopens still
    /// filtered is a dialog that looks half-empty for no visible reason.
    /// </summary>
    public string Query {
        get => _query;
        set {
            if (SetField(ref _query, value)) {
                Raise(nameof(Groups));
            }
        }
    }

    public IReadOnlyList<HotkeyGroup> Groups => HotkeyCatalog.Filter(_query);
}


/// <summary>The session log, the debug menu and putting every setting back.</summary>
public sealed class DebugSettingsCategory : SettingsCategoryViewModel {
    public DebugSettingsCategory(SettingsViewModel owner)
        : base(Strings.SettingsCategoryDebug, owner, guide: "esli-chto-to-poshlo-ne-tak") { }
}
