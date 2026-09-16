using Wander.Core.Persistence;

namespace Wander.Core.Tests;

/// <summary>
/// Shares a collection with <see cref="TempFilesTests"/>: both reassign the
/// static root, and xUnit runs classes in parallel unless told otherwise.
/// </summary>
[Collection("AppPaths")]
public class AppPathsTests {
    [Fact]
    public void Yield_IsOffUnlessAsked() {
        try {
            AppPaths.Resolve(Array.Empty<string>());

            Assert.False(AppPaths.Yields);
        } finally {
            AppPaths.Resolve(Array.Empty<string>());
        }
    }

    [Fact]
    public void Yield_ComesFromTheCommandLine() {
        try {
            AppPaths.Resolve(new[] { "--YIELD" });

            Assert.True(AppPaths.Yields);
            Assert.Equal("default", AppPaths.Source);
        } finally {
            AppPaths.Resolve(Array.Empty<string>());
        }
    }
}
