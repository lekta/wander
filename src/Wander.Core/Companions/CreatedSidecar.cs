using System.Security.Cryptography;
using Wander.Core.FileSystem;

namespace Wander.Core.Companions;

/// <summary>
/// What a sidecar Wander created held when it was written, for the undo of
/// the creation. The name Wander gives it (<c>IMG.xmp</c>, <c>IMG.CR2.pp3</c>)
/// is the one Lightroom, ACR and RawTherapee write too, so by the time
/// <c>Ctrl+Z</c> comes the file may hold somebody's develop recipe
/// (audit 2026-11-18, F1).
/// </summary>
public sealed record CreatedSidecar(long Length, string Hash, int Rank, int ColorLabel) {

    public static CreatedSidecar Of(byte[] content, int rank, int colorLabel) {
        return new CreatedSidecar(content.LongLength, HashOf(content), rank, colorLabel);
    }


    /// <summary>
    /// The undo, given what the file holds now and what of it could be read
    /// (<paramref name="read"/> null - it could not). The same bytes - the
    /// file goes. Other bytes - the file stays, and of the two fields Wander
    /// wrote only those still holding Wander's value are taken out: one the
    /// editor changed is the editor's. A file that does not read is not
    /// touched.
    /// </summary>
    public CreatedSidecarUndo PlanUndo(byte[] current, SidecarRating? read) {
        if (current.LongLength == Length && HashOf(current) == Hash) {
            return CreatedSidecarUndo.DeleteFile;
        }
        if (read is null) {
            return CreatedSidecarUndo.KeepAll;
        }

        var strip = new List<RatingField>(2);
        if (read.Rank == Rank) {
            strip.Add(RatingField.Rank);
        }
        // A label outside the five colours reads as index 0, the number of
        // "no colour": the name is what tells the two apart.
        string? name = ColorLabel == 0 ? null : ColorLabels.Name(ColorLabel);
        if (read.ColorLabel == ColorLabel && string.Equals(read.ColorLabelName, name, StringComparison.OrdinalIgnoreCase)) {
            strip.Add(RatingField.ColorLabel);
        }

        return new CreatedSidecarUndo(false, strip);
    }


    private static string HashOf(byte[] content) {
        return Convert.ToHexString(SHA256.HashData(content));
    }
}


/// <summary>What the undo of a created sidecar does: delete the file, or keep it and take these fields out.</summary>
public sealed record CreatedSidecarUndo(bool Delete, IReadOnlyList<RatingField> Strip) {
    public static readonly CreatedSidecarUndo DeleteFile = new(true, Array.Empty<RatingField>());
    public static readonly CreatedSidecarUndo KeepAll = new(false, Array.Empty<RatingField>());
}
