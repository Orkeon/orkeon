using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Infrastructure.Flows.Visualization;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering flow visualization services.
/// </summary>
public static class FlowVisualizationExtensions
{
    /// <summary>
    /// Adds flow visualization services (graph serializer and execution tracker).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonFlowVisualization(this IServiceCollection services)
    {
        // FlowGraphSerializer exposes only static utility methods (Serialize / ExportToMermaid)
        // and is not resolved from DI anywhere; it therefore needs no registration.
        services.TryAddSingleton<FlowExecutionTracker>();
        return services;
    }
}
