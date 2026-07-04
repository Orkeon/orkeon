using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Plugins;

/// <summary>
/// Contract implemented by Orkeon plugins. A plugin is discovered in a configurable
/// plugin directory, loaded in an isolated, collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, and given a chance to
/// contribute services (tools, LLM providers, memory providers, ...) to the host's
/// dependency-injection container.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must expose a public parameterless constructor — the loader
/// instantiates them via <see cref="Activator.CreateInstance(Type)"/>.
/// </para>
/// <para>
/// <b>Trust boundary</b>: loading a plugin executes arbitrary code with the full
/// privileges of the host process. There is no sandbox in v1 — only load plugins
/// from trusted sources. See <c>docs/architecture/plugins.md</c>.
/// </para>
/// </remarks>
public interface IOrkeonPlugin
{
    /// <summary>
    /// Stable, human-readable plugin name (e.g. <c>my-company.weather-tools</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plugin version (informational; SemVer recommended).
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Contributes the plugin's services to the host container. Called once per plugin
    /// instance during <c>AddOrkeonPlugins(...)</c>, before the service provider is built.
    /// Typical contributions: <c>services.AddSingleton&lt;IBaseTool, MyTool&gt;()</c>.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    void ConfigureServices(IServiceCollection services);
}
