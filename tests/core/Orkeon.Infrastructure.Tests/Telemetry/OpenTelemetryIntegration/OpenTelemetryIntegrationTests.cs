using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Orkeon.Application.Telemetry;
using Orkeon.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using Orkeon.Tests.Shared.Telemetry;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Telemetry;

/// <summary>
/// Serialises the tests that set an <c>OTEL_*</c> variable in the process: a container composed
/// while one is set attaches an OTLP exporter.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OtelEnvironmentCollection
{
    public const string Name = "otel-environment";
}

[Collection(OtelEnvironmentCollection.Name)]
public class OpenTelemetryIntegrationTests
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private readonly OpenTelemetryIntegrationTestsFixture _fixture = new();

    /// <summary>
    /// GAP-36, decision 5: the hosts keep the environment variables without a prefix as their lowest
    /// layer, and this is why. The OTLP exporter reads its <c>OTEL_*</c> settings from the container's
    /// <see cref="IConfiguration"/>, not from the process: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, which a
    /// .NET Aspire AppHost sets without a prefix, reaches the exporter through that layer.
    /// </summary>
    [Fact]
    public void The_unprefixed_OTLP_endpoint_reaches_the_exporter_through_the_unprefixed_layer()
    {
        Environment.SetEnvironmentVariable(OtlpEndpointVariable, "http://127.0.0.1:4999");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddEnvironmentVariables("ORKEON_")
                .Build();

            Assert.Equal(new Uri("http://127.0.0.1:4999"), ExporterEndpoint(configuration));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OtlpEndpointVariable, null);
        }
    }

    /// <summary>
    /// The other half of the proof (GAP-36, option 5 (b) set aside): without that layer the route
    /// still sees the variable in the process and attaches an exporter, which then sends to its own
    /// default, never to the endpoint Aspire set.
    /// </summary>
    [Fact]
    public void Without_the_unprefixed_layer_the_exporter_misses_the_endpoint()
    {
        Environment.SetEnvironmentVariable(OtlpEndpointVariable, "http://127.0.0.1:4999");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables("ORKEON_").Build();

            Assert.Equal(new Uri("http://localhost:4317"), ExporterEndpoint(configuration));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OtlpEndpointVariable, null);
        }
    }

    /// <summary>
    /// The endpoint of the exporter <c>AddOrkeonTelemetry</c> attaches from the environment, as the
    /// exporter builds its options: from the container's configuration, which a host registers.
    /// </summary>
    private static Uri ExporterEndpoint(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddOrkeonTelemetry(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsFactory<OtlpExporterOptions>>().Create(Options.DefaultName).Endpoint;
    }

    private static bool NamesOrkeonMetrics(ServiceDescriptor d) =>
        d.ServiceType.Name == "OrkeonMetrics"
        || d.ImplementationType?.Name == "OrkeonMetrics"
        || d.ImplementationInstance?.GetType().Name == "OrkeonMetrics";

    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Names { get; } = new();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                if (metric.MeterName == OrkeonMeter.Name)
                    Names.Add(metric.Name);
            }

            return ExportResult.Success;
        }
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldExportTheOrkeonMeter_WhenAddingTelemetry()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });
        _fixture.WithMockProviders(services);
        using var exporter = new CollectingExporter();
        using var reader = new BaseExportingMetricReader(exporter);
        services.ConfigureOpenTelemetryMeterProvider(b => b.AddReader(reader));

        var infrastructure = OpenTelemetryIntegrationTestsFixture.CreateServicesWithInfrastructure();

        Assert.DoesNotContain(services, NamesOrkeonMetrics);
        Assert.DoesNotContain(infrastructure, NamesOrkeonMetrics);

        using var sp = services.BuildServiceProvider();
        using var meterProvider = sp.GetRequiredService<MeterProvider>();

        OrkeonMeter.RecordLlmCall("provider-ch03", "model-ch03", 0.5, null, 3, 4);
        meterProvider.ForceFlush();

        Assert.Contains("orkeon.llm.calls", exporter.Names);
        Assert.Contains("gen_ai.client.operation.duration", exporter.Names);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldRegisterNoProviderNorExporter_WhenTelemetryIsDisabled()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "false"
        });

        Assert.DoesNotContain(services, NamesOrkeonMetrics);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(MeterProvider) || d.ServiceType == typeof(TracerProvider));
        Assert.DoesNotContain(services, d => d.ServiceType.Name.Contains("Exporter", StringComparison.Ordinal)
            || (d.ImplementationType?.Name.Contains("Exporter", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public void ShouldRegisterHealthChecks_WhenAddingTelemetry()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });
        _fixture.WithMockProviders(services);

        var sp = services.BuildServiceProvider();

        var healthCheckService = sp.GetService<HealthCheckService>();
        Assert.NotNull(healthCheckService);
    }

    [Fact]
    public async Task ShouldResolveThroughDI_WhenCheckingHealth()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true",
            ["Telemetry:MaxMemoryMB"] = "100000"
        });
        _fixture.WithMockProviders(services, "TestProvider");

        var sp = services.BuildServiceProvider();

        var healthCheckService = sp.GetRequiredService<HealthCheckService>();
        var report = await healthCheckService.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.True(report.Entries.ContainsKey("llm_provider"));
        Assert.True(report.Entries.ContainsKey("memory_provider"));
        Assert.True(report.Entries.ContainsKey("system_resources"));
        Assert.Equal(HealthStatus.Healthy, report.Entries["llm_provider"].Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries["memory_provider"].Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries["system_resources"].Status);
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void ShouldListenToScriptingSource_WhenTelemetryTracerIsBuilt()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });
        using var sp = services.BuildServiceProvider();
        using var tracer = sp.GetRequiredService<OpenTelemetry.Trace.TracerProvider>();

        using var source = new ActivitySource("Orkeon.Scripting");
        using var activity = source.StartActivity("probe");

        Assert.NotNull(activity);
        Assert.True(activity!.Recorded);
    }

}
