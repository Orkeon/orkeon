using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Runs;
using Orkeon.Infrastructure.Telemetry.HealthChecks;

namespace Orkeon.Host.Api;

/// <summary>
/// <c>crew_registry</c> (readiness): the daemon hosts at least one crew and still admits runs. Local: it reads the
/// registry and nothing else.
/// </summary>
internal sealed class CrewRegistryHealthCheck(CrewHostRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (registry.AdmissionClosed)
            return Task.FromResult(HealthCheckResult.Unhealthy("The host is stopping: no run is admitted."));

        return Task.FromResult(registry.Crews.Count == 0
            ? HealthCheckResult.Unhealthy("No crew is hosted.")
            : HealthCheckResult.Healthy($"{registry.Crews.Count} crew(s) hosted."));
    }
}

/// <summary>
/// <c>state_store</c> (readiness): the checkpoint store answers. One read of a session that does not exist; a store that
/// throws is not ready. Healthy when the host registers no store.
/// </summary>
internal sealed class StateStoreHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var store = services.GetService<IStateStore>();
        if (store is null)
            return HealthCheckResult.Healthy("No state store is registered.");

        try
        {
            await store.GetAsync("orkeon-readyz-probe", cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("The state store answers.");
        }
#pragma warning disable CA1031 // A probe reports any failure of the store as "not ready".
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("The state store does not answer.", ex);
        }
    }
}

/// <summary>
/// <c>run_history</c> (readiness): the run history answers (one query of one run). Healthy when the history is off.
/// </summary>
internal sealed class RunHistoryHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var history = services.GetService<IRunHistoryReader>();
        if (history is null)
            return HealthCheckResult.Healthy("The run history is off.");

        try
        {
            await history.ListAsync(new RunQuery { Limit = 1 }, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("The run history answers.");
        }
#pragma warning disable CA1031 // A probe reports any failure of the history as "not ready".
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("The run history does not answer.", ex);
        }
    }
}

/// <summary>Registers the daemon's readiness checks beside the framework's (<c>AddOrkeonHealthChecks</c>).</summary>
internal static class HostHealthChecks
{
    public static IServiceCollection AddHostReadinessChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<CrewRegistryHealthCheck>("crew_registry", tags: [HealthCheckExtensions.ReadyTag])
            .AddCheck<StateStoreHealthCheck>("state_store", tags: [HealthCheckExtensions.ReadyTag])
            .AddCheck<RunHistoryHealthCheck>("run_history", tags: [HealthCheckExtensions.ReadyTag]);
        return services;
    }
}
