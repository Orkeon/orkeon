using System.Diagnostics;
using Orkeon.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
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

    [Fact]
    public void ShouldRegisterOrkeonMetrics_WhenAddingTelemetry()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });
        _fixture.WithMockProviders(services);

        var sp = services.BuildServiceProvider();

        var metrics = sp.GetService<OrkeonMetrics>();
        Assert.NotNull(metrics);
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
    public void ShouldStillRegisterMetrics_WhenTelemetryIsDisabled()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "false"
        });

        var sp = services.BuildServiceProvider();

        var metrics = sp.GetService<OrkeonMetrics>();
        Assert.NotNull(metrics);
    }

    [Fact]
    public void ShouldRegisterTelemetry_WhenAddingInfrastructureWithConfiguration()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithInfrastructure(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true"
        });

        var sp = services.BuildServiceProvider();

        var metrics = sp.GetService<OrkeonMetrics>();
        Assert.NotNull(metrics);
    }

    [Fact]
    public void ShouldStillRegisterMetrics_WhenAddingInfrastructureWithoutConfiguration()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithInfrastructure();

        var sp = services.BuildServiceProvider();

        var metrics = sp.GetService<OrkeonMetrics>();
        Assert.NotNull(metrics);
    }

    [Fact]
    public async Task ShouldWrapCallsWithSpans_WhenUsingTelemetryChatClient()
    {
        var activities = new List<Activity>();
        using var listener = OpenTelemetryIntegrationTestsFixture.CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var innerClient = OpenTelemetryIntegrationTestsFixture.CreateMockChatClientWithResponse(
            "Test response", inputTokens: 10, outputTokens: 5, totalTokens: 15);

        using var metrics = new OrkeonMetrics();
        using var client = OpenTelemetryIntegrationTestsFixture.CreateTelemetryChatClient(innerClient, metrics, "TestProvider");

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")],
            new ChatOptions { ModelId = TestModelName }, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal("Test response", response.Text);

        var llmActivities = activities
            .Where(a => a.DisplayName.StartsWith("chat", StringComparison.Ordinal)
                && a.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmProvider)?.ToString() == "testprovider")
            .ToList();
        Assert.Single(llmActivities);
        Assert.Equal("testprovider", llmActivities[0].GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmProvider));
        Assert.Equal(TestModelName, llmActivities[0].GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmModel));
    }

    [Fact]
    public async Task ShouldRecordException_WhenInnerClientThrows()
    {
        var activities = new List<Activity>();
        using var listener = OpenTelemetryIntegrationTestsFixture.CreateActivityListener(activities);
        ActivitySource.AddActivityListener(listener);

        using var innerClient = OpenTelemetryIntegrationTestsFixture.CreateMockChatClientWithException(new HttpRequestException("API error"));

        using var metrics = new OrkeonMetrics();
        using var client = OpenTelemetryIntegrationTestsFixture.CreateTelemetryChatClient(innerClient, metrics, "TestProvider");

        await Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hello")], cancellationToken: TestContext.Current.CancellationToken));

        var llmActivities = activities
            .Where(a => a.DisplayName.StartsWith("chat", StringComparison.Ordinal)
                && a.GetTagItem(global::Orkeon.Infrastructure.Telemetry.OrkeonDiagnosticTags.LlmProvider)?.ToString() == "testprovider")
            .ToList();
        Assert.Single(llmActivities);
        Assert.Equal(ActivityStatusCode.Error, llmActivities[0].Status);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenResolvingMetricsSingleton()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithInfrastructure();

        var sp = services.BuildServiceProvider();

        var metrics1 = sp.GetService<OrkeonMetrics>();
        var metrics2 = sp.GetService<OrkeonMetrics>();
        Assert.Same(metrics1, metrics2);
    }
}
