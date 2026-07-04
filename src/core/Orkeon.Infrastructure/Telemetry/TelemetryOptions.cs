namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// Configuration options for Orkeon telemetry.
/// Bind from the "Telemetry" section in appsettings.json.
/// </summary>
public class TelemetryOptions
{
    /// <summary>
    /// Gets or sets whether telemetry is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the OTLP (OpenTelemetry Protocol) exporter endpoint.
    /// When set, traces and metrics are exported to this endpoint.
    /// Example: "http://localhost:4317" for gRPC or "http://localhost:4318" for HTTP.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets whether to export telemetry to the console (for development).
    /// Default is false.
    /// </summary>
    public bool ExportToConsole { get; set; }

    /// <summary>
    /// Gets or sets whether to enable Prometheus metrics endpoint.
    /// Default is false.
    /// </summary>
    public bool PrometheusEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the maximum memory threshold in MB for health checks.
    /// Default is 2048 MB.
    /// </summary>
    public int MaxMemoryMB { get; set; } = 2048;
}
