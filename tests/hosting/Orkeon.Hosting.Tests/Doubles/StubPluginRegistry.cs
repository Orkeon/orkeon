using Orkeon.Plugins;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>The registry <c>AddOrkeonPlugins</c> leaves in the container of a host that loaded no plugin.</summary>
internal sealed class StubPluginRegistry : IPluginRegistry
{
    public IReadOnlyList<LoadedPluginAssembly> Assemblies { get; } = [];

    public IReadOnlyList<IOrkeonPlugin> Plugins { get; } = [];

    public IReadOnlyList<PluginLoadFailure> Failures { get; } = [];
}
