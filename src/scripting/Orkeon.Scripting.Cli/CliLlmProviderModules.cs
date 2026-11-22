using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// The provider modules the <c>orkeon</c> tool ships, for the commands that build a provider without a
/// container (<c>orkeon doctor</c>, <c>orkeon llm probe</c>), and registered in every host the tool
/// builds (<see cref="AddTo"/>): one list for both. None ships yet.
/// </summary>
internal static class CliLlmProviderModules
{
    /// <summary>The modules, by the key a section names them with in <c>Provider</c>.</summary>
    internal static IReadOnlyList<ILlmProviderModule> All { get; } = [];

    /// <summary>Registers <see cref="All"/> in a host's container, where the provider factory reads them.</summary>
    internal static void AddTo(IServiceCollection services)
    {
        foreach (var module in All)
            services.AddSingleton(module);
    }
}
