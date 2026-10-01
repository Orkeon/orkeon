using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Domain.Common;

namespace Orkeon.Interop.AgentFramework.DependencyInjection;

/// <summary>Registers the Agent Framework interop in an Orkeon host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="ICrewAgentFactory"/> that turns a crew into a
    /// <see cref="CrewAgent"/> running every turn in a scope of its own. The factory holds the
    /// host's <see cref="IServiceScopeFactory"/> only — never a scoped service — so it resolves
    /// under scope validation. Everything else in the interop is a plain constructor call.
    /// </summary>
    public static IServiceCollection AddOrkeonAgentFramework(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICrewAgentFactory>(sp => new CrewAgentFactory(sp.GetRequiredService<IServiceScopeFactory>()));
        return services;
    }

    private sealed class CrewAgentFactory(IServiceScopeFactory scopes) : ICrewAgentFactory
    {
        public CrewAgent Create(
            Func<IServiceProvider, CancellationToken, Task<CrewId>> loadCrew,
            string name,
            string? description = null) =>
            new(scopes, loadCrew, name, description);
    }
}

/// <summary>Creates <see cref="CrewAgent"/>s that run their crew in a scope per turn.</summary>
public interface ICrewAgentFactory
{
    /// <summary>
    /// A MAF agent whose every turn opens a scope, lets <paramref name="loadCrew"/> register the
    /// crew in it (the crew, agent and task repositories are scoped) and kicks that crew off
    /// through the scope's orchestrator.
    /// </summary>
    /// <param name="loadCrew">Registers the crew in the turn's scope and returns its id.</param>
    /// <param name="name">The agent name; the agent id is <c>orkeon-crew-&lt;name&gt;</c>.</param>
    /// <param name="description">What the agent does, for MAF orchestrators that route by description.</param>
    CrewAgent Create(Func<IServiceProvider, CancellationToken, Task<CrewId>> loadCrew, string name, string? description = null);
}
