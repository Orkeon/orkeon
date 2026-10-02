using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;
using CrewStatus = Orkeon.Domain.Crew.ValueObjects.CrewStatus;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using ExecutionStatus = Orkeon.Domain.Crew.ExecutionStatus;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// GAP-32 decision 4 — how a run ends, through the host's own composition (<c>AddOrkeonApplication</c>
/// + <c>AddOrkeonInfrastructure</c>): a task counts once, by its final outcome, so a graph run that
/// recovered on a retry is a success; and a run that fails before its strategy — a memory that cannot
/// work, a crew the repository does not hold — reaches the execution hook once, like a run its strategy
/// failed.
/// </summary>
public sealed class CrewFailureKickoffTests
{
    private const string TwoTaskCrew = """
        name: launch-desk
        goal: Publish the launch notes
        process: PROCESS
        agents:
          writer:
            role: Writer
            goal: Write the launch notes
        tasks:
          draft:
            description: Draft the launch notes
            expected_output: The draft
            agent: writer
          review:
            description: Review the launch notes
            expected_output: The review
            agent: writer
        """;

    private const string RememberingCrew = """
        name: news-desk
        goal: Publish the AI news summary
        process: sequential
        memory: true
        agents:
          analyst:
            role: Analyst
            goal: Summarize the news
        tasks:
          summary:
            description: Summarize this week's AI news for the newsletter
            expected_output: Five bullet points about the AI news
            agent: analyst
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_graph_task_that_fails_then_succeeds_on_its_retry_leaves_a_successful_run_counting_each_task_once()
    {
        var events = new RecordingDispatcher();
        var draftCalls = 0;
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) =>
        {
            var drafting = messages.Any(m => m.Content.Contains("Draft the launch notes", StringComparison.Ordinal));
            if (drafting && Interlocked.Increment(ref draftCalls) == 1)
                throw new HttpRequestException("upstream hiccup");
            return new LlmResponse { Content = drafting ? "The draft." : "The review.", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 };
        });
        await using var container = Host(vendor, events);
        await using var scope = container.CreateAsyncScope();
        var (crew, output) = await RunAsync(scope.ServiceProvider, TwoTaskCrew.Replace("PROCESS", "graph", StringComparison.Ordinal));

        Assert.True(output.Succeeded, output.Error);
        Assert.Equal(2, draftCalls);
        var completed = Assert.Single(events.Dispatched.OfType<CrewExecutionCompletedEvent>());
        Assert.Equal(2, completed.CompletedTasks);
        Assert.DoesNotContain(events.Dispatched, e => e is CrewExecutionFailedEvent);
        Assert.Equal(ExecutionStatus.Succeeded, crew.Executions[^1].Status);
        Assert.Equal(2, crew.Executions[^1].CompletedTasks);
    }

    [Fact]
    public async Task A_run_that_fails_before_its_strategy_reaches_the_execution_hook_once_with_its_cause()
    {
        // The memory is checked before the first task (GAP-30): no strategy runs, so none told the
        // hook — AUTO_SUMMARY.md, orkeon-host and the --events stream heard nothing of the failure.
        var hook = new RecordingHook();
        var vendor = new MockLlmProvider { Name = "vendor" };
        await using var container = Host(vendor, new RecordingDispatcher(), hook);
        await using var scope = container.CreateAsyncScope();

        var (_, output) = await RunAsync(scope.ServiceProvider, RememberingCrew);

        Assert.False(output.Succeeded);
        var failure = Assert.Single(hook.Failures);
        Assert.Equal(CrewHookStatus.Failed, failure.Snapshot.Status);
        Assert.Contains("no semantic embedding provider is configured", failure.Snapshot.FailureReason, StringComparison.Ordinal);
        Assert.Equal(output.Error, failure.Snapshot.FailureReason);
        Assert.NotNull(failure.Cause);
        Assert.Equal(0, hook.Completions);
        Assert.Equal(0, vendor.ChatCallCount);
    }

    [Fact]
    public async Task A_crew_the_repository_does_not_hold_reaches_the_execution_hook_once()
    {
        var hook = new RecordingHook();
        await using var container = Host(new MockLlmProvider(), new RecordingDispatcher(), hook);
        await using var scope = container.CreateAsyncScope();
        var missing = CrewId.Create();

        var output = await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            missing, CrewInput.Empty("launch"), Ct);

        Assert.False(output.Succeeded);
        var failure = Assert.Single(hook.Failures);
        Assert.Equal(missing.ToString(), failure.Snapshot.CrewId);
        Assert.Contains("not found", failure.Snapshot.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_that_fails_reaches_the_execution_hook_once()
    {
        // The strategy reports its own failure; the orchestrator must not report it a second time.
        var hook = new RecordingHook();
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((messages, _) => new LlmResponse
        {
            Content = messages.Any(m => m.Content.Contains("Review the launch notes", StringComparison.Ordinal)) ? string.Empty : "The draft.",
            PromptTokens = 3,
            CompletionTokens = 1,
            TokensUsed = 4,
        });
        await using var container = Host(vendor, new RecordingDispatcher(), hook);
        await using var scope = container.CreateAsyncScope();

        var (crew, output) = await RunAsync(scope.ServiceProvider, TwoTaskCrew.Replace("PROCESS", "sequential", StringComparison.Ordinal));

        Assert.False(output.Succeeded);
        var failure = Assert.Single(hook.Failures);
        Assert.Equal(output.Error, failure.Snapshot.FailureReason);
        Assert.Equal(0, hook.Completions);
        Assert.Equal(CrewStatus.Failed, crew.Status);
    }

    private static ServiceProvider Host(MockLlmProvider vendor, RecordingDispatcher events, RecordingHook? hook = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddSingleton(events);
        services.AddScoped<IDomainEventHandler<CrewExecutionCompletedEvent>, RecordingHandler>();
        services.AddScoped<IDomainEventHandler<CrewExecutionFailedEvent>, RecordingHandler>();
        if (hook is not null)
            services.AddSingleton<ICrewExecutionHook>(hook);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<(DomainCrew Crew, CrewOutput Output)> RunAsync(IServiceProvider sp, string yaml)
    {
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        var output = await sp.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, CrewInput.Empty("launch"), Ct);
        return (crew, output);
    }

    private sealed class RecordingDispatcher
    {
        private readonly ConcurrentQueue<DomainEvent> _dispatched = new();

        public IReadOnlyList<DomainEvent> Dispatched => [.. _dispatched];

        public void Record(DomainEvent domainEvent) => _dispatched.Enqueue(domainEvent);
    }

    private sealed class RecordingHandler(RecordingDispatcher recorder) :
        IDomainEventHandler<CrewExecutionCompletedEvent>,
        IDomainEventHandler<CrewExecutionFailedEvent>
    {
        public Task HandleAsync(CrewExecutionCompletedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);
        public Task HandleAsync(CrewExecutionFailedEvent domainEvent, CancellationToken cancellationToken = default) => Record(domainEvent);

        private Task Record(DomainEvent domainEvent)
        {
            recorder.Record(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>What the execution hook heard of the crew's end.</summary>
    private sealed class RecordingHook : ICrewExecutionHook
    {
        private readonly ConcurrentQueue<(CrewExecutionSnapshot Snapshot, Exception? Cause)> _failures = new();
        private int _completions;

        public IReadOnlyList<(CrewExecutionSnapshot Snapshot, Exception? Cause)> Failures => [.. _failures];

        public int Completions => Volatile.Read(ref _completions);

        public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

        public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

        public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref _completions);
            return Task.CompletedTask;
        }

        public Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct)
        {
            _failures.Enqueue((isPartial, ex));
            return Task.CompletedTask;
        }
    }
}
