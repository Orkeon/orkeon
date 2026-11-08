using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Discovery;

/// <summary>
/// The layout discovery recognizes: one sub-folder per plugin holding <c>plugin.json</c> and the entry
/// assembly it names. A flat dll is not a plugin. The dlls here are opaque bytes, never loaded.
/// </summary>
public class PluginDiscoveryLayoutTests
{
    private static string Manifest(string entry = "Gamma.dll", string name = "gamma") =>
        $$"""{ "name": "{{name}}", "version": "1.0.0", "orkeonVersion": "1.0.0-rc.1", "entryAssembly": "{{entry}}", "tools": [] }""";

    private static Task<IReadOnlyList<PluginAssemblyCandidate>> Discover(
        PluginDirectoryFixture fixture, OrkeonPluginsOptions? options = null) =>
        new PluginAssemblyDiscovery(fixture.FileSystem)
            .DiscoverAsync(options ?? new OrkeonPluginsOptions(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task DiscoverAsync_FlatDll_IsRefusedNotPassedOverUnsaid()
    {
        using var fixture = new PluginDirectoryFixture();
        var alpha = fixture.AddOpaqueFile("alpha.dll");
        var upper = fixture.AddOpaqueFile("UPPER.DLL");
        fixture.AddOpaqueFile("notes.txt");

        var candidates = await Discover(fixture);

        Assert.Equal([upper, alpha], candidates.Select(c => c.VirtualPath).Order(StringComparer.Ordinal));
        Assert.All(candidates, c => Assert.Contains("lies flat", c.ManifestFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_FolderWithManifest_YieldsTheEntryAssemblyAndItsManifest()
    {
        using var fixture = new PluginDirectoryFixture();
        var entry = fixture.AddOpaqueFile("gamma/Gamma.dll");
        fixture.AddOpaqueFile("gamma/Helper.dll");
        fixture.AddOpaqueFile("gamma/plugin.json", Manifest());

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Equal(entry, candidate.VirtualPath);
        Assert.Null(candidate.ManifestFailure);
        Assert.Equal("gamma", candidate.Manifest?.Name);
        Assert.StartsWith(fixture.PhysicalRoot, candidate.PhysicalPath, StringComparison.Ordinal);
        Assert.True(File.Exists(candidate.PhysicalPath));
    }

    [Fact]
    public async Task DiscoverAsync_EntryAssemblyNeedNotBeNamedAfterTheFolder()
    {
        using var fixture = new PluginDirectoryFixture();
        var entry = fixture.AddOpaqueFile("weather/Acme.Weather.Plugin.dll");
        fixture.AddOpaqueFile("weather/plugin.json", Manifest("Acme.Weather.Plugin.dll"));

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Equal(entry, candidate.VirtualPath);
    }

    [Fact]
    public async Task DiscoverAsync_FolderWithAnAssemblyButNoManifest_IsRefusedNotIgnored()
    {
        using var fixture = new PluginDirectoryFixture();
        var dll = fixture.AddOpaqueFile("delta/delta.dll");

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Null(candidate.Manifest);
        Assert.Equal(dll, candidate.VirtualPath);
        Assert.Contains("plugin.json", candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_FolderWithAnyDllButNoManifest_IsRefused()
    {
        using var fixture = new PluginDirectoryFixture();
        var dll = fixture.AddOpaqueFile("acme/Foo.dll");

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Equal(dll, candidate.VirtualPath);
        Assert.Contains("plugin.json", candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_FolderWithNeitherManifestNorAssembly_IsIgnored()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("logs/today.txt");
        fixture.AddOpaqueFile("data/Helper.txt");

        Assert.Empty(await Discover(fixture));
    }

    [Fact]
    public async Task DiscoverAsync_MalformedManifest_IsRefusedWithItsReason()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("gamma/Gamma.dll");
        fixture.AddOpaqueFile("gamma/plugin.json", "{ not json");

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Null(candidate.Manifest);
        Assert.Contains("plugin.json", candidate.ManifestFailure, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.PhysicalRoot, candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_ManifestMissingARequiredField_IsRefused()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("gamma/Gamma.dll");
        fixture.AddOpaqueFile("gamma/plugin.json", """{ "version": "1.0.0", "orkeonVersion": "1.0", "entryAssembly": "Gamma.dll" }""");

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Contains("name", candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_EntryAssemblyNamedButAbsent_IsRefused()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("gamma/plugin.json", Manifest("Missing.dll"));

        var candidate = Assert.Single(await Discover(fixture));

        Assert.Contains("Missing.dll", candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_EntryAssemblyOutsideTheFolder_IsRefused()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("outside.dll");
        fixture.AddOpaqueFile("gamma/plugin.json", Manifest("../outside.dll"));

        // The flat outside.dll is refused on its own; the folder's manifest is refused for pointing at it.
        var candidate = Assert.Single(await Discover(fixture), c => c.VirtualPath.StartsWith("/plugins/gamma/", StringComparison.Ordinal));

        Assert.NotNull(candidate.ManifestFailure);
        Assert.Contains("entryAssembly", candidate.ManifestFailure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsCandidatesSortedByVirtualPath()
    {
        using var fixture = new PluginDirectoryFixture();
        foreach (var dir in new[] { "zeta", "alpha", "mid" })
        {
            fixture.AddOpaqueFile($"{dir}/{dir}.dll");
            fixture.AddOpaqueFile($"{dir}/plugin.json", Manifest($"{dir}.dll", dir));
        }

        var candidates = await Discover(fixture);

        Assert.Equal(
            ["/plugins/alpha/alpha.dll", "/plugins/mid/mid.dll", "/plugins/zeta/zeta.dll"],
            candidates.Select(static c => c.VirtualPath).ToArray());
    }

    [Fact]
    public async Task DiscoverAsync_SearchPattern_FiltersOnTheEntryAssemblyName()
    {
        using var fixture = new PluginDirectoryFixture();
        var acme = fixture.AddOpaqueFile("acme/Acme.Web.dll");
        fixture.AddOpaqueFile("acme/plugin.json", Manifest("Acme.Web.dll", "acme"));
        fixture.AddOpaqueFile("other/Other.dll");
        fixture.AddOpaqueFile("other/plugin.json", Manifest("Other.dll", "other"));

        var candidates = await Discover(fixture, new OrkeonPluginsOptions { SearchPattern = "Acme.*" });

        // The entry assembly that misses the pattern is refused, not skipped in silence.
        Assert.Equal(2, candidates.Count);
        Assert.Null(Assert.Single(candidates, c => c.VirtualPath == acme).ManifestFailure);
        var refused = Assert.Single(candidates, c => c.VirtualPath != acme);
        Assert.Contains("SearchPattern", refused.ManifestFailure, StringComparison.Ordinal);
    }
}
