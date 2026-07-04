using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Discovery;

/// <summary>
/// Discovery tests over a real temp directory mounted through
/// <c>DiskBackedFileSystemService</c> ("/plugins"). Discovery only lists files —
/// the dlls here are opaque bytes, never loaded.
/// </summary>
public class PluginAssemblyDiscoveryTests
{
    private static readonly string[] SortedDiscoveredPaths =
        ["/plugins/Mid/Mid.dll", "/plugins/alpha.dll", "/plugins/zeta.dll"];

    private static OrkeonPluginsOptions DefaultOptions() => new();

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

    [Fact]
    public async Task DiscoverAsync_FlatLayout_FindsTopLevelDlls()
    {
        using var fixture = new PluginDirectoryFixture();
        var alpha = fixture.AddOpaqueFile("alpha.dll");
        fixture.AddOpaqueFile("readme.txt");
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        var candidates = await discovery.DiscoverAsync(DefaultOptions(), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal(alpha, candidate.VirtualPath);
    }

    [Fact]
    public async Task DiscoverAsync_ConventionalLayout_FindsDirNameDllOnly()
    {
        using var fixture = new PluginDirectoryFixture();
        var gamma = fixture.AddOpaqueFile("Gamma/Gamma.dll");
        fixture.AddOpaqueFile("Gamma/Helper.dll");          // private dependency — not a candidate
        fixture.AddOpaqueFile("Delta/Unrelated.dll");       // no Delta/Delta.dll — not a candidate
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        var candidates = await discovery.DiscoverAsync(DefaultOptions(), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal(gamma, candidate.VirtualPath);
    }

    [Fact]
    public async Task DiscoverAsync_MixedLayouts_ReturnsAllSortedByVirtualPath()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("zeta.dll");
        fixture.AddOpaqueFile("alpha.dll");
        fixture.AddOpaqueFile("Mid/Mid.dll");
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        var candidates = await discovery.DiscoverAsync(DefaultOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(
            SortedDiscoveredPaths,
            candidates.Select(static c => c.VirtualPath).ToArray());
    }

    [Fact]
    public async Task DiscoverAsync_SearchPattern_FiltersBothLayouts()
    {
        using var fixture = new PluginDirectoryFixture();
        var acmeFlat = fixture.AddOpaqueFile("Acme.Tools.dll");
        fixture.AddOpaqueFile("Other.Tools.dll");
        var acmeDir = fixture.AddOpaqueFile("Acme.Web/Acme.Web.dll");
        fixture.AddOpaqueFile("ThirdParty/ThirdParty.dll");
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);
        var options = new OrkeonPluginsOptions { SearchPattern = "Acme.*" };

        var candidates = await discovery.DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.VirtualPath == acmeFlat);
        Assert.Contains(candidates, c => c.VirtualPath == acmeDir);
    }

    [Fact]
    public async Task DiscoverAsync_DllExtension_IsCaseInsensitive()
    {
        using var fixture = new PluginDirectoryFixture();
        var upper = fixture.AddOpaqueFile("UPPER.DLL");
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        var candidates = await discovery.DiscoverAsync(DefaultOptions(), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.Equal(upper, candidate.VirtualPath);
    }

    [Fact]
    public async Task DiscoverAsync_PhysicalPath_IsMountResolvedAndExists()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("alpha.dll");
        var discovery = new PluginAssemblyDiscovery(fixture.FileSystem);

        var candidates = await discovery.DiscoverAsync(DefaultOptions(), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(candidates);
        Assert.StartsWith(fixture.PhysicalRoot, candidate.PhysicalPath, StringComparison.Ordinal);
        Assert.True(File.Exists(candidate.PhysicalPath));
    }
}
