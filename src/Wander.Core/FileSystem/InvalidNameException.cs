namespace Wander.Core.FileSystem;

/// <summary>
/// A name no file or folder can have on Windows
/// (<see cref="Rename.RenamePlanner.IsValidName"/>): a separator, a drive,
/// <c>..</c>, a device such as <c>CON</c>. What the user typed, so refused
/// in words, not reported as a failure of the disk.
/// </summary>
public sealed class InvalidNameException : ArgumentException {
    public InvalidNameException(string name) : base($"Not a valid name: '{name}'") {
        Name = name;
    }


    /// <summary>The name as it was given.</summary>
    public string Name { get; }
}
