using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

namespace Orkeon.Plugins.Tests.Doubles;

/// <summary>Marker service registered by <see cref="FakeAlphaPlugin"/>.</summary>
public sealed class AlphaPluginMarker;

/// <summary>
/// Hand-written test double implementing <see cref="IOrkeonPlugin"/>.
/// The test assembly that contains it doubles as the plugin-assembly fixture
/// (it is copied into a temp plugin directory and loaded through the real
/// <see cref="PluginLoader"/>).
/// NOTE: count-sensitive tests derive the expected number of plugins from the
/// assembly itself (<c>FixtureAssembly.CountPluginTypes()</c>) — adding another
/// <see cref="IOrkeonPlugin"/> implementation to this test project is safe but
/// should stay deliberate.
/// </summary>
public sealed class FakeAlphaPlugin : IOrkeonPlugin
{
    /// <summary>Number of times <see cref="ConfigureServices"/> ran (plain field, doubles convention).</summary>
    public int ConfigureServicesCallCount;

    public string Name => "fake-alpha";

    public string Version => "1.2.3";

    public void ConfigureServices(IServiceCollection services)
    {
        ConfigureServicesCallCount++;
        services.AddSingleton<AlphaPluginMarker>();
    }
}
