using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

namespace Orkeon.Plugins.Tests.Doubles;

/// <summary>Marker service registered by <see cref="FakeBetaPlugin"/>.</summary>
public sealed class BetaPluginMarker;

/// <summary>
/// Second hand-written <see cref="IOrkeonPlugin"/> double, so fixture assemblies
/// exercise the multi-plugin-per-assembly path.
/// </summary>
public sealed class FakeBetaPlugin : IOrkeonPlugin
{
    public string Name => "fake-beta";

    public string Version => "2.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<BetaPluginMarker>();
    }
}
