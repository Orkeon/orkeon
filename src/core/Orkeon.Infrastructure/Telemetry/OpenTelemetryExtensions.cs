using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orkeon.Infrastructure.Telemetry.HealthChecks;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// Extension methods to register Orkeon OpenTelemetry telemetry services.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds Orkeon telemetry services including OpenTelemetry tracing, metrics, and health checks.
    /// Configuration is read from the "Telemetry" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Bind TelemetryOptions
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection("Telemetry"));

        var options = new TelemetryOptions();
        configuration.GetSection("Telemetry").Bind(options);

        if (!options.Enabled)
        {
            // Register OrkeonMetrics as singleton even when telemetry is disabled
            // so that instrumented code can still call it without null checks
            services.TryAddSingleton<OrkeonMetrics>();
            return services;
        }

        // Register OrkeonMetrics as singleton
        services.TryAddSingleton<OrkeonMetrics>();

        // Configure OpenTelemetry
        var otelBuilder = services.AddOpenTelemetry();

        // Configure resource
        otelBuilder.ConfigureResource(resource =>
        {
            resource.AddService(
                serviceName: OrkeonDiagnostics.ServiceName,
                serviceVersion: OrkeonDiagnostics.ServiceVersion);
        });

        // Configure tracing
        otelBuilder.WithTracing(tracing =>
        {
            tracing.AddSource(OrkeonDiagnostics.AllSourceNames);
            tracing.AddHttpClientInstrumentation();

            if (!string.IsNullOrEmpty(options.OtlpEndpoint))
            {
                tracing.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.OtlpEndpoint);
                });
            }

            if (options.ExportToConsole)
            {
                tracing.AddConsoleExporter();
            }
        });

        // Configure metrics
        otelBuilder.WithMetrics(metrics =>
        {
            metrics.AddMeter(OrkeonMetrics.MeterName);
            metrics.AddRuntimeInstrumentation();
            metrics.AddHttpClientInstrumentation();

            if (!string.IsNullOrEmpty(options.OtlpEndpoint))
            {
                metrics.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.OtlpEndpoint);
                });
            }

            if (options.ExportToConsole)
            {
                metrics.AddConsoleExporter();
            }
        });

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<LlmProviderHealthCheck>("llm_provider")
            .AddCheck<MemoryProviderHealthCheck>("memory_provider")
            .AddCheck<SystemResourcesHealthCheck>("system_resources");

        return services;
    }
}
