using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Monitoring;
using Orkeon.Infrastructure.Monitoring;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering monitoring backend services
/// (metrics aggregation and trace exploration).
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): the monitoring backend is NOT registered by
/// <c>AddOrkeonInfrastructure()</c>. Hosts that expose aggregated metrics or trace
/// exploration call <c>AddOrkeonMonitoring(...)</c> explicitly.
/// See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class MonitoringExtensions
{
    /// <summary>
    /// Opt-in: adds Orkeon monitoring services (metrics aggregation and trace explorer).
    /// Optionally binds <see cref="MonitoringOptions"/> from the <c>Orkeon:Monitoring</c>
    /// configuration section.
    /// </summary>
    /// <remarks>
    /// Self-contained: both services only require logging and options.
    /// </remarks>
    public static IServiceCollection AddOrkeonMonitoring(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            services.AddOptions<MonitoringOptions>()
                .Bind(configuration.GetSection("Orkeon:Monitoring"));
        }
        else
        {
            services.TryAddSingleton(Options.Create(new MonitoringOptions()));
        }

        services.TryAddSingleton<IMetricsAggregation, MetricsAggregationService>();
        services.TryAddSingleton<ITraceExplorer, TraceExplorerService>();

        return services;
    }
}
