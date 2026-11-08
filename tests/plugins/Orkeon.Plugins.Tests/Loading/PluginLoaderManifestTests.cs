using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Loading;

/// <summary>
/// What the loader and <c>AddOrkeonPlugins</c> refuse about a manifest: none at all, a version range
/// the running Orkeon is outside of, a name or version at odds with the plugin, a tool not declared.
/// </summary>
public class PluginLoaderManifestTests
{
    private static readonly string[] Prefixes = ["Orkeon.", "Microsoft.Extensions."];

    private static PluginLoader Loader() => new(Prefixes, "1.0.0-rc.6");

    [Fact]
    public void Load_ManifestNamingAnEntryType_ActivatesOnlyThatType()
    {
        using var fixture = new PluginDirectoryFixture();
        var manifest = PluginDirectoryFixture.FixtureManifest() with { EntryType = typeof(Doubles.FakeAlphaPlugin).FullName };
        var candidate = fixture.AddFixtureCandidate("p", manifest);
        Doubles.FakeCountingPlugin.Constructed = 0;

        var loaded = Loader().Load(candidate);

        Assert.Equal("fake-alpha", Assert.Single(loaded.Plugins).Name);
        // The assembly is loaded in the plugin's own context: the counter is read from that copy.
        var counted = loaded.Assembly.GetType(typeof(Doubles.FakeCountingPlugin).FullName!)!;
        var constructed = (int)counted.GetProperty(nameof(Doubles.FakeCountingPlugin.Constructed))!.GetValue(null)!;
        Assert.Equal(0, constructed);
    }

    [Fact]
    public void Load_ManifestNamingAnUnknownEntryType_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var manifest = PluginDirectoryFixture.FixtureManifest() with { EntryType = "No.Such.Plugin" };
        var candidate = fixture.AddFixtureCandidate("p", manifest);

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Contains("No.Such.Plugin", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CandidateWithoutManifest_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddPluginFolder("p"));

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Equal(candidate.VirtualPath, ex.VirtualPath);
        Assert.Contains("plugin.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CandidateDiscoveryRefused_ThrowsWithTheDiscoveryReason()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddPluginFolder("p")) with { ManifestFailure = "the manifest is malformed" };

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Contains("the manifest is malformed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_OrkeonVersionOutOfRange_ThrowsAndLoadsNothing()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate(
            manifest: PluginDirectoryFixture.FixtureManifest(orkeonVersion: "[99.0.0,)"));

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Contains("[99.0.0,)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(candidate.VirtualPath, ex.VirtualPath);
    }

    [Fact]
    public void Load_CompatibleManifest_ActivatesOnlyThePluginItNames()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate();

        var loaded = Loader().Load(candidate);
        try
        {
            // The fixture assembly holds several IOrkeonPlugin types; the manifest names one.
            var plugin = Assert.Single(loaded.Plugins);
            Assert.Equal("fake-alpha", plugin.Name);
            Assert.Equal("1.2.3", plugin.Version);
            Assert.Equal("fake-alpha", loaded.Manifest.Name);
        }
        finally
        {
            loaded.Unload();
        }
    }

    [Fact]
    public void Load_ManifestNamingNoPluginOfTheAssembly_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate(manifest: PluginDirectoryFixture.FixtureManifest(name: "someone-else"));

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Contains("someone-else", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ManifestVersionDifferentFromThePlugins_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate(manifest: PluginDirectoryFixture.FixtureManifest(version: "9.9.9"));

        var ex = Assert.Throws<PluginManifestException>(() => Loader().Load(candidate));

        Assert.Contains("9.9.9", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1.2.3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrkeonPlugins_ToolNotDeclared_ThrowsAndLeavesNoRegistration()
    {
        using var fixture = new PluginDirectoryFixture();
        var entry = fixture.AddPluginFolder(
            "sample-plugin",
            PluginDirectoryFixture.FixtureManifest(name: "sample-plugin", version: "1.0.0", tools: "another.tool"));
        var services = new ServiceCollection();

        var ex = Assert.Throws<PluginManifestException>(() => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.Equal(entry, ex.VirtualPath);
        Assert.Contains(SamplePlugin.ToolName, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(IBaseTool));
    }

    [Fact]
    public void AddOrkeonPlugins_NoToolDeclaredAtAll_RefusesARegisteredTool()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddPluginFolder("sample-plugin", PluginDirectoryFixture.FixtureManifest(name: "sample-plugin", version: "1.0.0"));

        Assert.Throws<PluginManifestException>(() => new ServiceCollection().AddOrkeonPlugins(fixture.FileSystem));
    }

    [Fact]
    public void AddOrkeonPlugins_SamplePlugin_LoadsAndItsDeclaredToolIsRegistered()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddSamplePlugin();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem);

        Assert.Single(services, static d => d.ServiceType == typeof(IBaseTool));
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Equal("sample-plugin", Assert.Single(registry.Plugins).Name);
        Assert.Empty(registry.Failures);
    }

    [Fact]
    public void AddOrkeonPlugins_FlatDllNextToTheSample_IsNotLoaded_AndIsRecorded()
    {
        using var fixture = new PluginDirectoryFixture();
        var flat = fixture.AddFlatFixturePlugin("Flat.dll");
        fixture.AddSamplePlugin();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.ContinueOnError = true);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Equal("sample-plugin", Assert.Single(registry.Plugins).Name);
        Assert.Equal(flat, Assert.Single(registry.Failures).VirtualPath);
        Assert.DoesNotContain(services, static d => d.ServiceType.FullName == typeof(Doubles.AlphaPluginMarker).FullName);
    }

    [Fact]
    public void AddOrkeonPlugins_FlatDll_FailsFastByDefault()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddFlatFixturePlugin("Flat.dll");
        var services = new ServiceCollection();

        var error = Assert.ThrowsAny<PluginLoadException>(() => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.Contains("lies flat", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrkeonPlugins_FolderWithADllAndNoManifest_IsRecordedAsAFailureWithContinueOnError()
    {
        using var fixture = new PluginDirectoryFixture();
        var dll = fixture.AddOpaqueFile("acme/Foo.dll");
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.ContinueOnError = true);

        using var provider = services.BuildServiceProvider();
        var failure = Assert.Single(provider.GetRequiredService<IPluginRegistry>().Failures);
        Assert.Equal(dll, failure.VirtualPath);
    }

    [Fact]
    public void AddOrkeonPlugins_ContinueOnError_RecordsEachManifestRefusalAndLoadsTheRest()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddOpaqueFile("a-nomanifest/a-nomanifest.dll");
        fixture.AddPluginFolder("b-outofrange", PluginDirectoryFixture.FixtureManifest(orkeonVersion: "[99.0.0,)"));
        fixture.AddPluginFolder(
            "c-undeclared",
            PluginDirectoryFixture.FixtureManifest(name: "sample-plugin", version: "1.0.0"));
        fixture.AddSamplePlugin("z-sample");
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.ContinueOnError = true);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Equal(3, registry.Failures.Count);
        Assert.Equal("sample-plugin", Assert.Single(registry.Plugins).Name);
    }
}
