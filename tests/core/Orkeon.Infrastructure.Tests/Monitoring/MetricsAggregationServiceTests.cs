using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Monitoring;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Monitoring;

[Collection("OrkeonMeter")]
public sealed class MetricsAggregationServiceTests : IClassFixture<MetricsAggregationServiceTestsFixture>, IDisposable
{
    private readonly MetricsAggregationServiceTestsFixture _fixture;
    private readonly MetricsAggregationService _service;

    public MetricsAggregationServiceTests(MetricsAggregationServiceTestsFixture fixture)
    {
        _fixture = fixture;
        _service = _fixture.CreateService();
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task GetCurrentMetricsAsync_ReturnsDefaultMetrics()
    {
        // Act — between two readings of the clock, never within a window of it (GAP-41)
        var before = DateTime.UtcNow;
        var metrics = await _service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);
        var after = DateTime.UtcNow;

        // Assert
        Assert.NotNull(metrics);
        Assert.InRange(metrics.CapturedAt, before, after);
        Assert.Equal(0, metrics.TotalLlmCalls);
        Assert.Equal(0, metrics.TotalToolExecutions);
        Assert.Equal(0, metrics.TotalTaskExecutions);
        Assert.Equal(0, metrics.TotalCrewExecutions);
        Assert.Equal(0, metrics.ActiveCrews);
        Assert.Equal(0, metrics.ActiveTasks);
        // The cost is a process-wide total on the Orkeon meter: compare with the gauge as it stands.
        Assert.Equal(ReadCostGauge(), metrics.TotalCostUsd, 1e-9);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldReadCallsTasksAndCrews_AfterARun()
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => new LlmResponse { Content = "The notes.", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = container.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync($"""
            name: dashboard-{Guid.NewGuid():N}
            goal: Publish the notes
            process: sequential
            agents:
              writer:
                role: Writer
                goal: Write the notes
            tasks:
              draft:
                description: Draft the notes
                expected_output: The draft
                agent: writer
            """, TestContext.Current.CancellationToken);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("dashboard", new Dictionary<string, object>()), TestContext.Current.CancellationToken);
        var metrics = await _service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);

        Assert.True(output.Succeeded);
        Assert.True(metrics.TotalLlmCalls > 0);
        Assert.True(metrics.TotalTaskExecutions > 0);
        Assert.True(metrics.TotalCrewExecutions > 0);
    }

    private static double ReadCostGauge()
    {
        var value = 0.0;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Orkeon.Application.Telemetry.OrkeonMeter.Name
                && instrument.Name == "orkeon.cost.total_usd")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, m, _, _) => value = m);
        listener.Start();
        listener.RecordObservableInstruments();
        return value;
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task GetMetricsSnapshotAsync_ReturnsSnapshotWithTimestamp()
    {
        // Act
        var before = DateTime.UtcNow;
        var snapshot = await _service.GetMetricsSnapshotAsync(TestContext.Current.CancellationToken);
        var after = DateTime.UtcNow;

        // Assert
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Metrics);
        Assert.NotNull(snapshot.CountersByName);
        Assert.NotNull(snapshot.HistogramAverages);
        Assert.True(snapshot.SnapshotAt >= before && snapshot.SnapshotAt <= after,
            "SnapshotAt should be between the before and after timestamps");
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void Service_ImplementsIDisposable()
    {
        // Arrange
        using var service = _fixture.CreateService();

        // Act & Assert — should not throw
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }
}
