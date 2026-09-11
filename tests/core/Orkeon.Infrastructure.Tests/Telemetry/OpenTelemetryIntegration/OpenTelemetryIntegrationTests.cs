using System.Diagnostics;
using Orkeon.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public class OpenTelemetryIntegrationTests
{
    private readonly OpenTelemetryIntegrationTestsFixture _fixture = new();

    [Fact]
    public void ShouldRegisterOrkeonMetrics_WhenAddingTelemetry()
    {
        var services = OpenTelemetryIntegrationTestsFixture.CreateServicesWithTelemetry(new Dictionary<string, string?>
        {
            ["Telemetry:Enabled"] = "true",
            ["Telemetry:ExportToConsole"] = "false"
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
