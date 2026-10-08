using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orkeon.Infrastructure.Telemetry.HealthChecks;
using Orkeon.Application.Configuration;
using Orkeon.Constants.Configuration;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// Extension methods to register Orkeon OpenTelemetry telemetry services.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>The section bound to <see cref="TelemetryOptions"/>.</summary>
    private const string TelemetrySection = "Telemetry";

    /// <summary>
    /// Adds Orkeon telemetry services including OpenTelemetry tracing, metrics, and health checks.
    /// Configuration is read from the "Telemetry" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The section carries a key that is no setting any more (<c>ExportToConsole</c>,
    /// <c>PrometheusEndpoint</c>), or an <c>OtlpEndpoint</c> that is not an <c>http://</c> or
    /// <c>https://</c> address.
    /// </exception>
    public static IServiceCollection AddOrkeonTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(TelemetrySection);
        RefuseRemovedKeys(section);

        // Bind TelemetryOptions — read below at registration already; declared so its keys are
        // judged with every other section's at the host's start (GAP-40).
        services.AddOptions<TelemetryOptions>()
            .Bind(section)
            .DeclareSettings(TelemetrySection);

        var options = new TelemetryOptions();
        section.Bind(options);
        EnsureEndpointIsAnAddress(options.OtlpEndpoint);

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

        otelBuilder.WithTracing(tracing => ConfigureTracing(tracing, otlp));
        otelBuilder.WithMetrics(metrics => ConfigureMetrics(metrics, otlp));

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

    /// <summary>
    /// The keys of the section that are no settings any more (GAP-35), each with why. A key still
    /// written — whatever its value — is refused, naming what replaces it, rather than ignored.
    /// </summary>
    private static readonly (string Key, string Why)[] s_removedKeys =
    [
        ("ExportToConsole",
            "the console exporter wrote traces and metrics on stdout, which `--events jsonl`, the `--list-tools` "
            + "manifest and `orkeon mcp serve` reserve for the program that reads them; a C# host that wants the "
            + "console adds the exporter to its own AddOpenTelemetry()"),
        ("PrometheusEndpoint", "nothing ever served a Prometheus endpoint"),
    ];

    private static void RefuseRemovedKeys(IConfigurationSection section)
    {
        foreach (var (key, why) in s_removedKeys)
        {
            if (!section.GetSection(key).Exists())
                continue;

            throw new InvalidOperationException(
                $"Telemetry:{key} is not a setting any more: {why}. Send the telemetry to an OTLP collector — "
                + "Telemetry:OtlpEndpoint, the standard OTEL_EXPORTER_OTLP_ENDPOINT, or the .NET Aspire dashboard. "
                + "Remove the key.");
        }
    }

    /// <summary>
    /// An explicit endpoint must be an address: refused here, by its key, rather than as a bare
    /// <see cref="UriFormatException"/> when the exporter is built.
    /// </summary>
    private static void EnsureEndpointIsAnAddress(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
            return;

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Telemetry:OtlpEndpoint is '{endpoint}', which is not an address: write the collector's http:// or "
            + "https:// URL, such as http://localhost:4317.");
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, OtlpRoute otlp)
    {
        tracing.AddSource(OrkeonDiagnostics.AllSourceNames);
        tracing.AddHttpClientInstrumentation();

        if (otlp.Endpoint is { } endpoint)
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(endpoint));
        else if (otlp.Exports)
            tracing.AddOtlpExporter();
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics, OtlpRoute otlp)
    {
        metrics.AddMeter(OrkeonMetrics.MeterName);
        metrics.AddRuntimeInstrumentation();
        metrics.AddHttpClientInstrumentation();

        if (otlp.Endpoint is { } endpoint)
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(endpoint));
        else if (otlp.Exports)
            metrics.AddOtlpExporter();
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
            var fromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariableNames.OtlpEndpoint));
            return new OtlpRoute(endpoint, endpoint is not null || fromEnvironment);
        }
    }
}
