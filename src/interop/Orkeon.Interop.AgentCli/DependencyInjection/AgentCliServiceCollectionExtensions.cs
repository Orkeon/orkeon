using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Interop.AgentCli.DependencyInjection;

/// <summary>Registers the <c>agent-cli</c> provider module (ADR-020).</summary>
public static class AgentCliServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>agent-cli</c> provider module, bound to the <c>Llm:AgentCli</c> section, and declares
    /// that section's shape. A section naming <c>"Provider": "agent-cli"</c> is then built by it. Idempotent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    [Experimental(AgentCliDiagnostics.ExperimentalId, UrlFormat = AgentCliDiagnostics.ExperimentalUrl)]
    public static IServiceCollection AddOrkeonAgentCli(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AgentCliOptions>().BindConfiguration(AgentCliOptions.SectionName);
        services.TryAddSingleton(sp => new AgentCliProviderModule(sp.GetRequiredService<IOptions<AgentCliOptions>>().Value));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILlmProviderModule, AgentCliProviderModule>(
            sp => sp.GetRequiredService<AgentCliProviderModule>()));
        services.DeclareSettingsShape(AgentCliOptions.SectionName, typeof(AgentCliOptions));
        return services;
    }
}
