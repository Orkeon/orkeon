using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Telemetry;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// CH-02 (GAP-46) - through the host's own composition, a crew run counts the crew and each task once
/// at its end, with its outcome and a duration in seconds, keeps the active gauges balanced, with or
/// without an ActivityListener, and tags no id. Every test runs a crew name only it produces.
/// </summary>
[Collection("OrkeonMeter")]
public sealed class RunMetricsKickoffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string name, bool dependent = false) => $$"""
        name: {{name}}
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
          review:
            description: Review the notes
            expected_output: The review
            agent: writer
        {{(dependent ? """
          publish:
            description: Publish the notes
            expected_output: The publication
            agent: writer
            dependencies: [review]
        """ : "")}}
        """;

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCountTheCrewOnceAndEachTask_WhenACrewOfTwoTasksCompletes()
    {
        var name = NewName();
        using var capture = Capture(name);
        await using var container = Host(Vendor());
        await using var scope = container.CreateAsyncScope();

        var output = await RunAsync(scope.ServiceProvider, Yaml(name), Ct);

        Assert.True(output.Succeeded);
        var crews = capture.Of("orkeon.crew.executions");
        var crew = Assert.Single(crews);
        Assert.Equal(1, crew.Value);
        Assert.Equal("completed", crew.Tag("orkeon.outcome"));
        Assert.False(string.IsNullOrEmpty(crew.Tag("orkeon.crew.process")));
        var tasks = capture.Of("orkeon.task.executions");
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t =>
        {
            Assert.Equal("completed", t.Tag("orkeon.outcome"));
            Assert.Equal(name, t.Tag("orkeon.crew.name"));
            Assert.False(string.IsNullOrEmpty(t.Tag("orkeon.agent.role")));
        });
        Assert.Single(capture.Of("orkeon.crew.duration"));
        Assert.Equal(2, capture.Of("orkeon.task.duration").Count);
        Assert.All(capture.Of("orkeon.crew.duration").Concat(capture.Of("orkeon.task.duration")), m => Assert.True(m.Value >= 0));
        Assert.Equal("s", capture.UnitOf("orkeon.crew.duration"));
        Assert.Equal("s", capture.UnitOf("orkeon.task.duration"));
        Assert.Contains(capture.Of("orkeon.crew.active"), m => m.Value > 0);
        Assert.Equal(0, capture.Of("orkeon.crew.active").Sum(m => m.Value));
        Assert.Equal(0, capture.Of("orkeon.task.active").Sum(m => m.Value));
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCountFailed_WhenATaskFails()
    {
        var name = NewName();
        using var capture = Capture(name);
        await using var container = Host(FailingReviewVendor());
        await using var scope = container.CreateAsyncScope();

        var output = await RunAsync(scope.ServiceProvider, Yaml(name), Ct);

        Assert.False(output.Succeeded);
        Assert.Equal("failed", Assert.Single(capture.Of("orkeon.crew.executions")).Tag("orkeon.outcome"));
        Assert.Contains(capture.Of("orkeon.task.executions"), m => m.Tag("orkeon.outcome") == "failed");
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCountCancelled_WhenADependencyFailed()
    {
        var name = NewName();
        using var capture = Capture(name);
        await using var container = Host(FailingReviewVendor());
        await using var scope = container.CreateAsyncScope();

        var output = await RunAsync(scope.ServiceProvider, Yaml(name, dependent: true), Ct);

        Assert.False(output.Succeeded);
        var outcomes = capture.Of("orkeon.task.executions").Select(m => m.Tag("orkeon.outcome")).OrderBy(o => o, StringComparer.Ordinal).ToList();
        Assert.Equal(["cancelled", "completed", "failed"], outcomes);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCarryNoCrewNorTaskId_OnAnyMeasurement()
    {
        var okName = NewName();
        var koName = NewName();
        using var capture = Capture(okName, koName);
        await using var container = Host(FailingReviewVendor());
        await using var scope = container.CreateAsyncScope();

        await RunAsync(scope.ServiceProvider, Yaml(okName).Replace("Review the notes", "Check the notes"), Ct);
        await RunAsync(scope.ServiceProvider, Yaml(koName), Ct);

        var all = capture.All.Where(m => m.Instrument.StartsWith("orkeon.crew.", StringComparison.Ordinal)
            || m.Instrument.StartsWith("orkeon.task.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(all);
        Assert.Contains(all, m => m.Instrument == "orkeon.crew.executions");
        string[] allowed = ["orkeon.crew.name", "orkeon.crew.process", "orkeon.agent.role", "orkeon.outcome"];
        Assert.All(all, m => Assert.All(m.Tags.Keys, key => Assert.Contains(key, allowed)));
        Assert.Contains(all, m => m.Tag("orkeon.outcome") == "failed");
        Assert.Contains(all, m => m.Tag("orkeon.outcome") == "completed");
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCountTheCrewAndItsTasks_WhenNoActivityListenerIsPresent()
    {
        var name = NewName();
        using var capture = Capture(name);
        Assert.False(OrkeonActivitySources.Crew.HasListeners());
        Assert.False(OrkeonActivitySources.Task.HasListeners());
        await using var container = Host(Vendor());
        await using var scope = container.CreateAsyncScope();

        var output = await RunAsync(scope.ServiceProvider, Yaml(name), Ct);

        Assert.True(output.Succeeded);
        Assert.Equal("completed", Assert.Single(capture.Of("orkeon.crew.executions")).Tag("orkeon.outcome"));
        Assert.Equal(2, capture.Of("orkeon.task.executions").Count(m => m.Tag("orkeon.outcome") == "completed"));
        Assert.Equal(0, capture.Of("orkeon.crew.active").Sum(m => m.Value));
        Assert.Equal(0, capture.Of("orkeon.task.active").Sum(m => m.Value));
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldCountCancelledAndBringActiveBackToZero_WhenTheRunIsCancelledByItsToken()
    {
        var name = NewName();
        using var capture = Capture(name);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        await using var container = Host(vendor);
        await using var scope = container.CreateAsyncScope();

        var output = await RunAsync(scope.ServiceProvider, Yaml(name), cts.Token);

        Assert.False(output.Succeeded);
        Assert.Equal("cancelled", Assert.Single(capture.Of("orkeon.crew.executions")).Tag("orkeon.outcome"));
        Assert.Contains(capture.Of("orkeon.task.executions"), m => m.Tag("orkeon.outcome") == "cancelled");
        Assert.DoesNotContain(capture.Of("orkeon.task.executions"), m => m.Tag("orkeon.outcome") == "failed");
        Assert.Equal(0, capture.Of("orkeon.crew.active").Sum(m => m.Value));
        Assert.Equal(0, capture.Of("orkeon.task.active").Sum(m => m.Value));
    }

    private static string NewName() => "metrics-" + Guid.NewGuid().ToString("N");

    private static MockLlmProvider Vendor()
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => Answer("The notes."));
        return vendor;
    }

    private static MockLlmProvider FailingReviewVendor()
    {
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) => Answer(
            messages.Any(m => m.Content.Contains("Review the notes", StringComparison.Ordinal)) ? string.Empty : "The notes."));
        return vendor;
    }

    private static LlmResponse Answer(string content) => new()
    {
        Content = content,
        PromptTokens = 3,
        CompletionTokens = 1,
        TokensUsed = 4,
    };

    private static async Task<CrewOutput> RunAsync(IServiceProvider sp, string yaml, CancellationToken ct)
    {
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        DomainCrew crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("metrics", new Dictionary<string, object>()), ct);
    }

    private static ServiceProvider Host(MockLlmProvider vendor)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static MeterRecorder Capture(params string[] crewNames) =>
        new(OrkeonMeter.Name, "orkeon.crew.name", crewNames);
}
