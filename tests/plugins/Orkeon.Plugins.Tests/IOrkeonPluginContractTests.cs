using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins.Tests.Doubles;

namespace Orkeon.Plugins.Tests;

/// <summary>
/// Contract-level tests for <see cref="Orkeon.Plugins.IOrkeonPlugin"/>: metadata
/// exposure and service contribution, without any assembly loading involved.
/// </summary>
public class IOrkeonPluginContractTests
{
    [Fact]
    public void Plugin_ExposesNameAndVersionMetadata()
    {
        var plugin = new FakeAlphaPlugin();

        Assert.Equal("fake-alpha", plugin.Name);
        Assert.Equal("1.2.3", plugin.Version);
    }

    [Fact]
    public void ConfigureServices_ContributesServicesToTheCollection()
    {
        var plugin = new FakeAlphaPlugin();
        var services = new ServiceCollection();

        plugin.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<AlphaPluginMarker>());
        Assert.Equal(1, plugin.ConfigureServicesCallCount);
    }

    [Fact]
    public void ConfigureServices_CalledPerPlugin_DoesNotLeakAcrossCollections()
    {
        var plugin = new FakeBetaPlugin();
        var first = new ServiceCollection();
        var second = new ServiceCollection();

        plugin.ConfigureServices(first);

        Assert.Contains(first, static d => d.ServiceType == typeof(BetaPluginMarker));
        Assert.DoesNotContain(second, static d => d.ServiceType == typeof(BetaPluginMarker));
    }
}
