using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Telemetry.HealthChecks;

/// <summary>The health checks of the framework and the tags a probe selects them by (FWK-14).</summary>
public static class HealthCheckExtensions
{
    /// <summary>Tag of the checks that decide liveness: local, never an external service.</summary>
    public const string LiveTag = "live";

    /// <summary>Tag of the checks that decide readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Tag of the LLM check. It reaches an external service, so it is part of readiness only when the
    /// operator asks for it (<c>Orkeon:Host:Api:ReadinessTags</c> lists it).
    /// </summary>
    public const string LlmTag = "llm";

    /// <summary>
    /// Registers the framework's health checks, once, whether or not telemetry is enabled:
    /// <c>system_resources</c> (<see cref="LiveTag"/>), <c>memory_provider</c> (<see cref="ReadyTag"/>) and
    /// <c>llm_provider</c> (<see cref="LlmTag"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddOrkeonHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A check name registered twice makes the health service throw: a marker makes the call idempotent.
        if (services.Any(d => d.ServiceType == typeof(HealthChecksRegistered)))
            return services;
        services.AddSingleton<HealthChecksRegistered>();

        services.AddHealthChecks()
            .AddCheck<LlmProviderHealthCheck>("llm_provider", tags: [LlmTag])
            .AddCheck<MemoryProviderHealthCheck>("memory_provider", tags: [ReadyTag])
            .AddCheck<SystemResourcesHealthCheck>("system_resources", tags: [LiveTag]);
        return services;
    }

    private sealed class HealthChecksRegistered;
}
