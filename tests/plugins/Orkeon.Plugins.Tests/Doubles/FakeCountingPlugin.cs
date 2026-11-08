using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

namespace Orkeon.Plugins.Tests.Doubles;

/// <summary>
/// An <see cref="IOrkeonPlugin"/> double that counts its constructions on the constructing thread, so a test can
/// tell whether the loader activated it (the load is synchronous).
/// </summary>
public sealed class FakeCountingPlugin : IOrkeonPlugin
{
    [ThreadStatic]
    private static int s_constructed;

    public FakeCountingPlugin() => s_constructed++;

    public static int Constructed
    {
        get => s_constructed;
        set => s_constructed = value;
    }

    public string Name => "fake-counting";

    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
    }
}
