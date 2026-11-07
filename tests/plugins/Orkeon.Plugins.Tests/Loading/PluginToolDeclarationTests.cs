using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Doubles;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Loading;

/// <summary>
/// The tools a plugin registers against the ones its manifest declares: checked on the host's own
/// provider for a tool built by type, factory or key, at registration for a tool given as an instance;
/// a refused plugin is rolled back whole.
/// </summary>
public class PluginToolDeclarationTests
{
    private static ServiceCollection Compose(
        PluginDirectoryFixture fixture, string pluginName, params string[] tools)
    {
        fixture.AddPluginFolder(
            "p", PluginDirectoryFixture.FixtureManifest(name: pluginName, version: "1.0.0", tools: tools));
        var services = new ServiceCollection();
        services.AddOrkeonPlugins(fixture.FileSystem);

        // The host registers this after AddOrkeonPlugins, as it does with its own services.
        services.AddSingleton<StrongBox<int>>(new StrongBox<int>());
        return services;
    }

    [Theory]
    [InlineData("fake-tool-type")]
    [InlineData("fake-tool-factory")]
    public void ToolBuiltByTypeOrFactory_DeclaredAndNeedingALateHostService_IsBuiltOnceByTheHostProvider(string plugin)
    {
        using var fixture = new PluginDirectoryFixture();
        var services = Compose(fixture, plugin, FakeDependentTool.ToolName);

        using var provider = services.BuildServiceProvider();
        var tool = Assert.Single(provider.GetServices<IBaseTool>());

        Assert.Equal(FakeDependentTool.ToolName, tool.Name);
        Assert.Equal(1, provider.GetRequiredService<StrongBox<int>>().Value);
    }

    [Theory]
    [InlineData("fake-tool-type")]
    [InlineData("fake-tool-factory")]
    public void ToolBuiltByTypeOrFactory_NotDeclared_IsRefusedByTheHostProviderNamingThePlugin(string plugin)
    {
        using var fixture = new PluginDirectoryFixture();
        var services = Compose(fixture, plugin);

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<PluginManifestException>(() => provider.GetServices<IBaseTool>().ToList());

        Assert.Contains(plugin, ex.Message, StringComparison.Ordinal);
        Assert.Contains(FakeDependentTool.ToolName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyedTool_Declared_IsBuiltOnce()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = Compose(fixture, "fake-tool-keyed", FakeDependentTool.ToolName);

        using var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredKeyedService<IBaseTool>(FakeKeyedToolPlugin.Key);

        Assert.Equal(FakeDependentTool.ToolName, tool.Name);
        Assert.Equal(1, provider.GetRequiredService<StrongBox<int>>().Value);
    }

    [Fact]
    public void KeyedTool_NotDeclared_IsRefused()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = Compose(fixture, "fake-tool-keyed");

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<PluginManifestException>(
            () => provider.GetRequiredKeyedService<IBaseTool>(FakeKeyedToolPlugin.Key));

        Assert.Contains("fake-tool-keyed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolWhoseConstructorThrows_SurfacesAsAPluginLoadExceptionWithTheCauseInside()
    {
        using var fixture = new PluginDirectoryFixture();
        var services = Compose(fixture, "fake-tool-throwing", "throwing.tool");

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<PluginLoadException>(() => provider.GetServices<IBaseTool>().ToList());

        Assert.Contains("fake-tool-throwing", ex.Message, StringComparison.Ordinal);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public void RefusedInstanceTool_RemovesTheNonToolRegistrationsToo_AndUnloadsTheContext()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddPluginFolder(
            "p", PluginDirectoryFixture.FixtureManifest(name: "fake-mixed", version: "1.0.0"));
        var services = new ServiceCollection();
        services.AddSingleton(new StrongBox<string>("host"));
        var before = services.ToList();

        var ex = Assert.Throws<PluginManifestException>(() => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.Contains("fake-mixed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToList());
        Assert.DoesNotContain(services, static d => d.ServiceType.FullName == typeof(MixedPluginMarker).FullName);
        Assert.True(WaitUntilCollected(fixture.PhysicalRoot), "the refused plugin's load context was not unloaded");
    }

    [Fact]
    public void RefusedPlugin_RestoresAHostRegistrationItRemoved()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddPluginFolder(
            "p", PluginDirectoryFixture.FixtureManifest(name: "fake-replacing", version: "1.0.0"));
        var services = new ServiceCollection();
        services.AddSingleton(new StrongBox<string>("host"));
        var before = services.ToList();

        Assert.Throws<PluginManifestException>(() => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.Equal(before, services.ToList());
        Assert.Contains(services, static d => d.ServiceType == typeof(StrongBox<string>));
    }

    [Fact]
    public void ConfigureServicesThatThrows_IsRolledBackAndSurfacesAsAPluginLoadException()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddPluginFolder(
            "p", PluginDirectoryFixture.FixtureManifest(name: "fake-throwing-configure", version: "1.0.0"));
        var services = new ServiceCollection();
        services.AddSingleton(new StrongBox<string>("host"));
        var before = services.ToList();

        var ex = Assert.Throws<PluginLoadException>(() => services.AddOrkeonPlugins(fixture.FileSystem));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(before, services.ToList());
    }

    [Fact]
    public void ContinueOnError_RecordsARefusedInstanceToolAndLeavesNoRegistration()
    {
        using var fixture = new PluginDirectoryFixture();
        fixture.AddPluginFolder(
            "p", PluginDirectoryFixture.FixtureManifest(name: "fake-mixed", version: "1.0.0"));
        var services = new ServiceCollection();

        services.AddOrkeonPlugins(fixture.FileSystem, options => options.ContinueOnError = true);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        Assert.Single(registry.Failures);
        Assert.Empty(registry.Plugins);
        Assert.DoesNotContain(services, static d => d.ServiceType == typeof(IBaseTool));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference? ContextLoadedFrom(string physicalRoot)
    {
        foreach (var context in AssemblyLoadContext.All)
        {
            if (context is PluginLoadContext
                && context.Assemblies.Any(a => a.Location.StartsWith(physicalRoot, StringComparison.Ordinal)))
            {
                return new WeakReference(context);
            }
        }

        return null;
    }

    private static bool WaitUntilCollected(string physicalRoot)
    {
        var context = ContextLoadedFrom(physicalRoot);
        for (var attempt = 0; attempt < 50 && context is { IsAlive: true }; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return context is not { IsAlive: true };
    }
}
