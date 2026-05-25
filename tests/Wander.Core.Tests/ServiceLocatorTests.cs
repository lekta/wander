using Wander.Core;
using Wander.Core.FileSystem;
using Wander.Core.Tests.Fakes;

namespace Wander.Core.Tests;

public class ServiceLocatorTests : IDisposable {
    public ServiceLocatorTests() {
        ServiceLocator.Reset();
    }

    public void Dispose() {
        ServiceLocator.Reset();
    }


    [Fact]
    public void Register_And_Get_Roundtrip() {
        var fs = new FakeFileSystem();
        ServiceLocator.Register<IFileSystem>(fs);

        Assert.Same(fs, ServiceLocator.Get<IFileSystem>());
    }

    [Fact]
    public void Get_Unregistered_Throws() {
        Assert.Throws<InvalidOperationException>(() => ServiceLocator.Get<IFileSystem>());
    }

    [Fact]
    public void IsRegistered_ReflectsState() {
        Assert.False(ServiceLocator.IsRegistered<IFileSystem>());
        ServiceLocator.Register<IFileSystem>(new FakeFileSystem());
        Assert.True(ServiceLocator.IsRegistered<IFileSystem>());
    }

    [Fact]
    public void Register_Overwrites() {
        var first = new FakeFileSystem();
        var second = new FakeFileSystem();
        ServiceLocator.Register<IFileSystem>(first);
        ServiceLocator.Register<IFileSystem>(second);

        Assert.Same(second, ServiceLocator.Get<IFileSystem>());
    }
}
