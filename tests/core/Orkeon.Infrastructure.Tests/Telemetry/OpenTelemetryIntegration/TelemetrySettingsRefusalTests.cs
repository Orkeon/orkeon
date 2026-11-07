using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.Telemetry;

/// <summary>
/// An <c>OtlpEndpoint</c> that is no address is refused by its key instead of failing as a bare
/// <see cref="UriFormatException"/> when the exporter is built — in a C# host as in the runners.
/// </summary>
public sealed class TelemetrySettingsRefusalTests
{
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
    [Trait("BR", "CH-14")]
    public void ShouldRefuseTheReference_WhenTheOtlpEndpointIsWrittenAsOne()
    {
        const string path = "Telemetry:OtlpEndpoint";
        var correction = Orkeon.Constants.Configuration.SettingPlaceholders.CorrectionFor(path);
        Assert.NotNull(correction);

        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
            {
                [path] = "${OTLP}",
            }));

        Assert.Contains(Orkeon.Constants.Configuration.OperatorMessages.SettingPlaceholderRefused(path, correction), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("${OTLP}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_endpoint_is_accepted()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317",
        });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OpenTelemetry.Metrics.MeterProvider));
    }
}
