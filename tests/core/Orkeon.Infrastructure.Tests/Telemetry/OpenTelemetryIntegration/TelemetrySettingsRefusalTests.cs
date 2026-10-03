using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.Telemetry;

/// <summary>
/// GAP-35 — <c>Telemetry:ExportToConsole</c> attached OpenTelemetry's console exporter, which writes
/// on stdout — where <c>--events jsonl</c>, the <c>--list-tools</c> manifest and
/// <c>orkeon mcp serve</c>'s protocol go; <c>Telemetry:PrometheusEndpoint</c> was bound and read by
/// nothing. Both are removed, and a configuration that still writes one — whatever its value — is
/// refused by its name, with what replaces it: an OTLP collector. An <c>OtlpEndpoint</c> that is no
/// address is refused by its key instead of failing as a bare <see cref="UriFormatException"/> when
/// the exporter is built — in a C# host as in the runners.
/// </summary>
public sealed class TelemetrySettingsRefusalTests
{
    [Theory]
    [InlineData("Telemetry:ExportToConsole", "true")]
    [InlineData("Telemetry:ExportToConsole", "false")]
    [InlineData("Telemetry:PrometheusEndpoint", "true")]
    public void A_removed_key_is_refused_naming_what_replaces_it(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?> { [key] = value }));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_removed_key_is_refused_with_telemetry_switched_off_too()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
            {
                ["Telemetry:Enabled"] = "false",
                ["Telemetry:ExportToConsole"] = "true",
            }));

        Assert.Contains("Telemetry:ExportToConsole", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::")]
    [InlineData("localhost:4317")]
    public void An_endpoint_that_is_no_address_is_refused_by_its_key(string endpoint)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
            {
                ["Telemetry:OtlpEndpoint"] = endpoint,
            }));

        Assert.Contains("Telemetry:OtlpEndpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains(endpoint, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_endpoint_is_accepted()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317",
        });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OrkeonMetrics));
    }
}
