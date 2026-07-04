using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Interfaces.Training;
using Orkeon.Infrastructure.Training;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering training framework services.
/// </summary>
public static class TrainingExtensions
{
    /// <summary>
    /// Registers training framework services: feedback collector, performance tracker, and training orchestrator.
    /// </summary>
    public static IServiceCollection AddOrkeonTraining(this IServiceCollection services)
    {
        services.TryAddSingleton<IFeedbackCollector, AutomaticFeedbackCollector>();
        services.TryAddSingleton<IAgentPerformanceTracker, AgentPerformanceTracker>();
        services.TryAddSingleton<ITrainingOrchestrator, TrainingOrchestrator>();
        return services;
    }
}
