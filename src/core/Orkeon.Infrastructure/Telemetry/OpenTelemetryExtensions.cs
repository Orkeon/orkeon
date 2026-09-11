using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Logs;
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

        // The standard OpenTelemetry environment is honoured as-is: a host launched by
        // .NET Aspire (or any collector that sets OTEL_EXPORTER_OTLP_ENDPOINT) gets an OTLP
        // exporter with no Orkeon-specific setting. The exporter reads the endpoint, the
        // protocol and the headers from the environment itself when no explicit endpoint
        // is configured, so nothing is copied here.
        var otlpFromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

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
            else if (otlpFromEnvironment)
            {
                tracing.AddOtlpExporter();
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
            else if (otlpFromEnvironment)
            {
                metrics.AddOtlpExporter();
            }

            if (options.ExportToConsole)
            {
                metrics.AddConsoleExporter();
            }
        });

        // Logs follow the same route: with an OTLP endpoint (explicit or from the
        // environment) the structured log records reach the same backend as the spans,
        // which is what makes a run readable in the Aspire dashboard's console and
        // structured-logs views next to its traces.
        if (!string.IsNullOrEmpty(options.OtlpEndpoint) || otlpFromEnvironment)
        {
            services.AddLogging(logging => logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;
                if (!string.IsNullOrEmpty(options.OtlpEndpoint))
                    otelLogging.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint));
                else
                    otelLogging.AddOtlpExporter();
            }));
        }

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<LlmProviderHealthCheck>("llm_provider")
            .AddCheck<MemoryProviderHealthCheck>("memory_provider")
            .AddCheck<SystemResourcesHealthCheck>("system_resources");

        return services;
    }
}
