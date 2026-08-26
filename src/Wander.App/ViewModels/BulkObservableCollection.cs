using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Wander.App.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be refilled in one
/// notification instead of one per item.
///
/// <para>
/// Opening a folder of five thousand files used to raise five thousand
/// collection-changed events, and the tile panel answered every one of them
/// by invalidating layout. The measurements it took in the middle of that
/// were of containers whose templates had not settled, so it sized its grid
/// from them and built four times the containers it needed — which is what
/// the pause on opening a big folder actually was.
/// </para>
///
/// <para>
/// <see cref="ReplaceAll"/> raises a single <c>Reset</c>. Everything else is
/// the ordinary collection, because the incremental path (a rename, a
/// delete) genuinely wants per-item events: that is what keeps the list from
/// blinking and the selection from being dropped.
/// </para>
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T> {
    public void ReplaceAll(IReadOnlyList<T> items) {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items) {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
