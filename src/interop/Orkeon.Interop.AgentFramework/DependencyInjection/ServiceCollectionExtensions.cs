using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;

namespace Orkeon.Interop.AgentFramework.DependencyInjection;

/// <summary>Registers the Agent Framework interop in an Orkeon host.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="ICrewAgentFactory"/> that turns any registered crew into a
    /// <see cref="CrewAgent"/>. Everything else in the interop is a plain constructor call.
    /// </summary>
    public static IServiceCollection AddOrkeonAgentFramework(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICrewAgentFactory>(sp => new CrewAgentFactory(sp.GetRequiredService<ICrewOrchestrationService>()));
        return services;
    }

    private sealed class CrewAgentFactory(ICrewOrchestrationService orchestrator) : ICrewAgentFactory
    {
        public CrewAgent Create(CrewId crewId, string? name = null, string? description = null) =>
            new(orchestrator, crewId, name, description);

        public CrewAgent Create(Crew crew) => new(orchestrator, crew);
    }
}

/// <summary>Creates <see cref="CrewAgent"/>s for crews the host has registered.</summary>
public interface ICrewAgentFactory
{
    /// <summary>A MAF agent that runs the crew registered under <paramref name="crewId"/>.</summary>
    CrewAgent Create(CrewId crewId, string? name = null, string? description = null);

    /// <summary>A MAF agent that runs <paramref name="crew"/>, described by its goal.</summary>
    CrewAgent Create(Crew crew);
}
