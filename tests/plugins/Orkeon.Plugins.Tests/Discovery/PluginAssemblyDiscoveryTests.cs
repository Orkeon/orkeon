using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Discovery;

/// <summary>
/// Argument and directory handling of discovery over a real temp directory mounted through
/// <c>DiskBackedFileSystemService</c> ("/plugins"). The layout itself is in
/// <see cref="PluginDiscoveryLayoutTests"/>.
/// </summary>
public class PluginAssemblyDiscoveryTests
{
    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(static () => new PluginAssemblyDiscovery(null!));
    }

    [Fact]
    public async Task DiscoverAsync_NullOptions_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => discovery.DiscoverAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAsync_DirectoryNotVirtualPath_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);
        var options = new OrkeonPluginsOptions { Directory = "plugins" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => discovery.DiscoverAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAsync_MissingDirectory_ReturnsEmpty()
    {
        using var fixture = new PluginDirectoryFixture();
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);
        var options = new OrkeonPluginsOptions { Directory = "/plugins/does-not-exist" };

        var candidates = await discovery.DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Empty(candidates);
    }
}
