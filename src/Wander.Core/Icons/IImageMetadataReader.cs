namespace Wander.Core.Icons;

public interface IImageMetadataReader {
    /// <summary>Returns shot metadata for the image at <paramref name="path"/>, or null if not readable.</summary>
    ImageMetadata? Read(string path);
}
