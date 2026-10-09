using Wander.Core.Search;

namespace Wander.Core.Tests;

/// <summary>A found item's place, said from the folder the search started in.</summary>
public class ResultPathTests {
    [Theory]
    [InlineData(@"D:\Photos", @"D:\Photos\2024\May\a.jpg", @"2024\May\a.jpg")]
    [InlineData(@"D:\Photos\", @"D:\Photos\a.jpg", "a.jpg")]
    [InlineData(@"d:\photos", @"D:\Photos\Sub", "Sub")]
    [InlineData(@"D:\", @"D:\Photos\a.jpg", @"Photos\a.jpg")]
    [InlineData(@"D:\Photos", @"D:\PhotosOld\a.jpg", @"D:\PhotosOld\a.jpg")]
    [InlineData(@"D:\Photos", @"E:\a.jpg", @"E:\a.jpg")]
    [InlineData(null, @"D:\Photos\a.jpg", @"D:\Photos\a.jpg")]
    public void Relative_IsThePathFromTheRoot(string? root, string path, string expected) {
        Assert.Equal(expected, ResultPath.Relative(root, path));
    }

    [Theory]
    [InlineData(@"D:\Photos", @"D:\Photos\2024\May\a.jpg", @"2024\May")]
    [InlineData(@"D:\Photos", @"D:\Photos\a.jpg", "")]
    [InlineData(@"D:\Photos\", @"D:\Photos\Sub\", "")]
    [InlineData(@"D:\", @"D:\a.jpg", "")]
    [InlineData(@"D:\", @"D:\Photos\a.jpg", "Photos")]
    [InlineData(@"D:\Photos", @"E:\Other\a.jpg", @"E:\Other")]
    public void Folder_IsTheParentFromTheRoot_EmptyRightInIt(string? root, string path, string expected) {
        Assert.Equal(expected, ResultPath.Folder(root, path));
    }
}
