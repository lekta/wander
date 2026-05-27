using System.IO;
using Wander.Core.Operations;

namespace Wander.App.ViewModels;

/// <summary>
/// Read-only view of one running file operation. Built fresh from an
/// <see cref="OperationSnapshot"/> each time the tracker fires Changed —
/// no internal mutation, so it's safe to rebuild the collection on each
/// tick without subscriber bookkeeping.
/// </summary>
public sealed class OperationViewModel {
    public OperationViewModel(OperationSnapshot snapshot) {
        Verb = snapshot.Verb;
        Completed = snapshot.Completed;
        Total = snapshot.Total;
        Percent = snapshot.Total > 0 ? (double)snapshot.Completed * 100.0 / snapshot.Total : 0.0;
        Current = string.IsNullOrEmpty(snapshot.CurrentPath) ? "" : Path.GetFileName(snapshot.CurrentPath);
        Description = string.IsNullOrEmpty(Current)
            ? $"{Verb} {Completed}/{Total}"
            : $"{Verb} {Completed}/{Total} — {Current}";
    }


    public string Verb { get; }
    public int Completed { get; }
    public int Total { get; }
    public double Percent { get; }
    public string Current { get; }
    public string Description { get; }
}
