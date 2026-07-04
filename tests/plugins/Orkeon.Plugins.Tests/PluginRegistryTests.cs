using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests;

public class PluginRegistryTests
{
    [Fact]
    public void NewRegistry_IsEmpty()
    {
        using var registry = new PluginRegistry();

        Assert.Empty(registry.Assemblies);
        Assert.Empty(registry.Plugins);
        Assert.Empty(registry.Failures);
    }

    [Fact]
    public void AddFailure_IsExposedThroughFailures()
    {
        using var registry = new PluginRegistry();
        var failure = new PluginLoadFailure
        {
            VirtualPath = "/plugins/broken.dll",
            Reason = "not a managed assembly",
        };

        registry.AddFailure(failure);

        var recorded = Assert.Single(registry.Failures);
        Assert.Equal(failure, recorded);
    }

    [Fact]
    public void Add_ExposesAssembliesAndFlattenedPlugins()
    {
        using var fixture = new PluginDirectoryFixture();
        var loader = new PluginLoader(new OrkeonPluginsOptions().SharedAssemblyPrefixes);
        var loaded = loader.Load(fixture.CandidateFor(fixture.AddFlatFixturePlugin()));
        using var registry = new PluginRegistry();

        registry.Add(loaded);

        Assert.Same(loaded, Assert.Single(registry.Assemblies));
        Assert.Equal(loaded.Plugins.Count, registry.Plugins.Count);
        Assert.Contains(registry.Plugins, static p => p.Name == "fake-alpha");
    }

    [Fact]
    public void UnloadAll_RequestsUnload_AndClearsTheRegistry()
    {
        using var fixture = new PluginDirectoryFixture();
        var loader = new PluginLoader(new OrkeonPluginsOptions().SharedAssemblyPrefixes);
        var loaded = loader.Load(fixture.CandidateFor(fixture.AddFlatFixturePlugin()));
        using var registry = new PluginRegistry();
        registry.Add(loaded);

        registry.UnloadAll();

        Assert.True(loaded.IsUnloadRequested);
        Assert.Empty(registry.Assemblies);
        Assert.Empty(registry.Plugins);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndUnloadsEverything()
    {
        using var fixture = new PluginDirectoryFixture();
        var loader = new PluginLoader(new OrkeonPluginsOptions().SharedAssemblyPrefixes);
        var loaded = loader.Load(fixture.CandidateFor(fixture.AddFlatFixturePlugin()));
        var registry = new PluginRegistry();
        registry.Add(loaded);

        registry.Dispose();
        registry.Dispose();

        Assert.True(loaded.IsUnloadRequested);
        Assert.Empty(registry.Assemblies);
    }
}
