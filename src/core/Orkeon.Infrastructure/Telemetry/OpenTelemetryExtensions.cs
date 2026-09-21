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
        if (!options.Enabled)
        {
            // Register OrkeonMetrics as singleton even when telemetry is disabled
            // so that instrumented code can still call it without null checks
            services.TryAddSingleton<OrkeonMetrics>();
            return services;
        }

        var otlp = OtlpRoute.Resolve(options);

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

        otelBuilder.WithTracing(tracing => ConfigureTracing(tracing, options, otlp));
        otelBuilder.WithMetrics(metrics => ConfigureMetrics(metrics, options, otlp));

        // Logs follow the same route: with an OTLP endpoint (explicit or from the
        // environment) the structured log records reach the same backend as the spans,
        // which is what makes a run readable in the Aspire dashboard's console and
        // structured-logs views next to its traces.
        if (otlp.Exports)
            services.AddLogging(logging => logging.AddOpenTelemetry(otelLogging => AddOtlpLogging(otelLogging, otlp)));

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<LlmProviderHealthCheck>("llm_provider")
            .AddCheck<MemoryProviderHealthCheck>("memory_provider")
            .AddCheck<SystemResourcesHealthCheck>("system_resources");

        return services;
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, TelemetryOptions options, OtlpRoute otlp)
    {
        tracing.AddSource(OrkeonDiagnostics.AllSourceNames);
        tracing.AddHttpClientInstrumentation();

        if (otlp.Endpoint is { } endpoint)
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(endpoint));
        else if (otlp.Exports)
            tracing.AddOtlpExporter();

        if (options.ExportToConsole)
            tracing.AddConsoleExporter();
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, TelemetryOptions options, OtlpRoute otlp)
    {
        metrics.AddMeter(OrkeonMetrics.MeterName);
        metrics.AddRuntimeInstrumentation();
        metrics.AddHttpClientInstrumentation();

        if (otlp.Endpoint is { } endpoint)
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(endpoint));
        else if (otlp.Exports)
            metrics.AddOtlpExporter();

        if (options.ExportToConsole)
            metrics.AddConsoleExporter();
    }

    private static void AddOtlpLogging(OpenTelemetryLoggerOptions otelLogging, OtlpRoute otlp)
    {
        otelLogging.IncludeFormattedMessage = true;
        otelLogging.IncludeScopes = true;
        if (otlp.Endpoint is { } endpoint)
            otelLogging.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(endpoint));
        else
            otelLogging.AddOtlpExporter();
    }

    /// <summary>
    /// Where the OTLP exporters send: the endpoint the settings name, else the one the
    /// standard OpenTelemetry environment names (read by the exporter itself), else nowhere.
    /// </summary>
    /// <param name="Endpoint">The settings' explicit endpoint, or null; parsed where the exporter is built, as before.</param>
    /// <param name="Exports">Whether an OTLP exporter is attached at all (explicit endpoint or environment).</param>
    private sealed record OtlpRoute(string? Endpoint, bool Exports)
    {
        public static OtlpRoute Resolve(TelemetryOptions options)
        {
            var endpoint = string.IsNullOrEmpty(options.OtlpEndpoint) ? null : options.OtlpEndpoint;
            var fromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
            return new OtlpRoute(endpoint, endpoint is not null || fromEnvironment);
        }
    }
}
