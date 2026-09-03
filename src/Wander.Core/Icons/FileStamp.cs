namespace Wander.Core.Icons;

/// <summary>
/// What a file looked like when its picture was taken: last-write time and
/// length. Two readings that differ mean the file is not the one the
/// thumbnail was made from.
///
/// <para>
/// Both halves are needed. The time alone misses a file rewritten by a tool
/// that preserves timestamps; the length alone misses an edit that happens
/// to keep the size. Together they are what the disk cache already keys on,
/// lifted into a value so the memory cache can hold the same fact - it is
/// keyed by path alone, and a path says nothing about the file behind it
/// having been replaced.
/// </para>
/// </summary>
public readonly record struct FileStamp(long Ticks, long Size) {
    /// <summary>The stamp of a listing row - the reading Wander already has.</summary>
    public static FileStamp Of(DateTime modifiedUtc, long? size) {
        return new FileStamp(modifiedUtc.Ticks, size ?? 0);
    }


    /// <summary>False for the default value - "nobody said", not "an empty file from year one".</summary>
    public bool IsKnown => Ticks != 0;
}
