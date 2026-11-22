using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Interop.AgentCli;
using Orkeon.Interop.AgentCli.DependencyInjection;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// The provider modules the <c>orkeon</c> tool ships: <c>agent-cli</c> (ADR-020, experimental). The commands
/// that build a provider without a container (<c>orkeon doctor</c>, <c>orkeon llm probe</c>) read
/// <see cref="All"/>, on the module's default options, or <see cref="For"/>, on the options a settings file gives; every host the tool builds registers the same modules
/// through <see cref="AddTo"/>, where their options bind from the host's settings (#472: a test refuses a file of the tool that
/// composes a host and registers none).
/// </summary>
internal static class CliLlmProviderModules
{
    /// <summary>The modules, by the key a section names them with in <c>Provider</c>.</summary>
    internal static IReadOnlyList<ILlmProviderModule> All { get; } = [new AgentCliProviderModule(new AgentCliOptions())];

    /// <summary>The module on the options <paramref name="configuration"/> holds for it (<c>Llm:AgentCli</c>); the caller disposes it.</summary>
    internal static AgentCliProviderModule For(IConfiguration configuration)
    {
        var options = new AgentCliOptions();
        configuration.GetSection(AgentCliOptions.SectionName).Bind(options);
        return new AgentCliProviderModule(options);
    }

    /// <summary>Registers the modules in a host's container, where the provider factory reads them.</summary>
    internal static void AddTo(IServiceCollection services) => services.AddOrkeonAgentCli();
}
