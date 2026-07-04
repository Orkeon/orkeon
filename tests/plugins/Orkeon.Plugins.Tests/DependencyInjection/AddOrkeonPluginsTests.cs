using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Doubles;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.DependencyInjection;

/// <summary>
/// End-to-end tests of the opt-in DI activation: discovery → isolated load →
/// plugin ConfigureServices contributions → IPluginRegistry registration.
/// </summary>
public class AddOrkeonPluginsTests
{
    [Fact]
    public void AddOrkeonPlugins_NullServices_Throws()
    {
        using var fixture = new PluginDirectoryFixture();

        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddOrkeonPlugins(fixture.FileSystem));
    }

    [Fact]
    public void AddOrkeonPlugins_NullFileSystem_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddOrkeonPlugins((IFileSystemService)null!));
    }

    [Fact]
    public void AddOrkeonPlugins_NullConfiguration_Throws()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddOrkeonPlugins(fixture.FileSystem, (IConfiguration)null!));
    }

    [Fact]
    public void AddOrkeonPlugins_EmptyDirectory_RegistersAnEmptyRegistry()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Empty(registry.Assemblies);
        Assert.Empty(registry.Failures);
    }

    [Fact]
    public void AddOrkeonPlugins_MissingDirectory_DoesNotThrow()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.Directory = "/plugins/none");

        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetRequiredService<IPluginRegistry>().Assemblies);
    }

    [Fact]
    public void AddOrkeonPlugins_LoadsPlugins_AndAppliesTheirRegistrations()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddFlatFixturePlugin();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem);

        // The plugin registered its marker service. The marker type comes from the
        // isolated load context: same full name as the host's, different identity.
        var alphaDescriptor = Assert.Single(
            services,
            d => d.ServiceType.FullName == typeof(AlphaPluginMarker).FullName);
        Assert.NotSame(typeof(AlphaPluginMarker), alphaDescriptor.ServiceType);
        Assert.Contains(
            services,
            d => d.ServiceType.FullName == typeof(BetaPluginMarker).FullName);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Single(registry.Assemblies);
        Assert.Contains(registry.Plugins, static p => p.Name == "fake-alpha");
        Assert.Contains(registry.Plugins, static p => p.Name == "fake-beta");
        Assert.Empty(registry.Failures);
    }

    [Fact]
    public void AddOrkeonPlugins_ConventionalLayout_IsLoadedToo()
    {
        using var fixture = new PluginDirectoryFixture();
        var virtualPath = fixture.AddConventionalFixturePlugin("Weather");
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        var assembly = Assert.Single(registry.Assemblies);
        Assert.Equal(virtualPath, assembly.VirtualPath);
    }

    [Fact]
    public void AddOrkeonPlugins_NonPluginAssembly_IsSkippedSilently()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddFlatNonPluginAssembly();
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Empty(registry.Assemblies);
        Assert.Empty(registry.Failures);
    }

    [Fact]
    public void AddOrkeonPlugins_BrokenAssembly_FailsFastByDefault()
    {
        using var fixture = new PluginDirectoryFixture();
        var garbage = fixture.AddOpaqueFile("garbage.dll");
        var services = new ServiceCollection();

        var ex = Assert.Throws<PluginLoadException>(
            () => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.Equal(garbage, ex.VirtualPath);
    }

    [Fact]
    public void AddOrkeonPlugins_ContinueOnError_RecordsFailure_AndLoadsTheRest()
    {
        using var fixture = new PluginDirectoryFixture();
        var garbage = fixture.AddOpaqueFile("a-garbage.dll"); // sorts before the real plugin
        fixture.AddFlatFixturePlugin("z-real-plugin.dll");
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.ContinueOnError = true);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        var failure = Assert.Single(registry.Failures);
        Assert.Equal(garbage, failure.VirtualPath);
        Assert.Single(registry.Assemblies);
        Assert.Contains(registry.Plugins, static p => p.Name == "fake-alpha");
    }

    [Fact]
    public void AddOrkeonPlugins_ConfigurationOverload_BindsThePluginsSection()
    {
        using var fixture = new PluginDirectoryFixture();
        var garbage = fixture.AddOpaqueFile("garbage.dll");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plugins:Directory"] = "/plugins",
                ["Plugins:ContinueOnError"] = "true",
            })
            .Build();
        var services = new ServiceCollection();

        // ContinueOnError=true came from configuration: the broken dll is recorded
        // instead of throwing — proof the "Plugins" section was bound.
        services.AddOrkeonPlugins(fixture.FileSystem, configuration);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        var failure = Assert.Single(registry.Failures);
        Assert.Equal(garbage, failure.VirtualPath);
    }
}
