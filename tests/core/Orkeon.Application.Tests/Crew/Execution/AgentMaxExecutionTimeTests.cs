using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// CH-01 - an agent's <c>MaxExecutionTime</c> bounds the task it runs, measured on the
/// orchestrator's <c>TimeProvider</c>: past it the task ends with
/// <see cref="AgentExitReason.ExecutionTimeExceeded"/> and a Warning names the agent and the limit.
/// On a manual clock: no test waits in real time.
/// </summary>
public sealed class AgentMaxExecutionTimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private enum Mode
    {
        Answer,
        WaitForCancel,
        WaitForRelease,
    }

    /// <summary>A chat client that answers, waits for its token to fall, or waits for the test.</summary>
    private sealed class WaitingChatClient(Mode mode, int toolCalls = 0) : IChatClient
    {
        private int _toolCalls = toolCalls;

        public System.Threading.Tasks.TaskCompletionSource Entered { get; } =
            new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Threading.Tasks.TaskCompletionSource Release { get; } =
            new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The deadline the running task held while the provider call was made.</summary>
        public TaskDeadline? SeenDeadline { get; private set; }

        public async System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            SeenDeadline = TaskDeadline.Current;
            if (_toolCalls > 0)
            {
                _toolCalls--;
                return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent($"call-{_toolCalls}", "lookup", new Dictionary<string, object?> { ["input"] = "x" })])]);
            }

            Entered.TrySetResult();
            if (mode == Mode.WaitForCancel)
                await System.Threading.Tasks.Task.Delay(Timeout.Infinite, cancellationToken);
            else if (mode == Mode.WaitForRelease)
                await Release.Task.WaitAsync(cancellationToken);

            return new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class LinesLogger : ILogger<ExecutionOrchestrator>
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public string[] Lines
        {
            get
            {
                lock (_gate)
                    return [.. _lines];
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
                _lines.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private static readonly SpyTool Lookup = new("lookup", result: "found");

    private static DomainAgent Agent(TimeSpan? limit, int? maxRpm = null)
    {
        var builder = new AgentBuilder().Role("Researcher").Goal("Answer the question").WithTool(Lookup);
        if (limit is { } max)
            builder.MaxExecutionTime(max);
        if (maxRpm is { } rpm)
            builder.MaxRpm(rpm);
        return builder.Build();
    }

    private static DomainTask NewTask() =>
        DomainTask.Create(TaskDescription.From("Answer the question"), ExpectedOutput.From("An answer"));

    private static SimpleExecutionContext Context() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    private static ExecutionOrchestrator Orchestrator(IChatClient client, ManualTimeProvider clock, ILogger<ExecutionOrchestrator>? logger = null) =>
        new(logger ?? new LinesLogger(), new ScriptedBasicLlmProvider(), client, [Lookup], new FakeFileSystemService())
        {
            TimeProvider = clock,
        };

    // A hang guard only: the run is driven by the manual clock, never by this bound.
    private static System.Threading.Tasks.Task<TaskResult> Bounded(System.Threading.Tasks.Task<TaskResult> run) =>
        run.WaitAsync(Polling.DefaultTimeout, Ct);

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldFailWithExecutionTimeExceeded_WhenTaskRunsPastAgentLimit()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.WaitForCancel);
        var orchestrator = Orchestrator(client, clock);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent(Limit), NewTask(), Context(), Ct);
        await client.Entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit + TimeSpan.FromTicks(1));
        var result = await Bounded(run);

        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.ExecutionTimeExceeded, result.ExitReason);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldNameAgentAndLimit_WhenTaskExceedsItsLimit()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.WaitForCancel);
        var logger = new LinesLogger();
        var orchestrator = Orchestrator(client, clock, logger);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent(Limit), NewTask(), Context(), Ct);
        await client.Entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit + TimeSpan.FromTicks(1));
        await Bounded(run);

        var line = Assert.Single(logger.Lines, l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("Researcher", line, StringComparison.Ordinal);
        Assert.Contains("30", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldCompleteNormally_WhenClockStopsJustBeforeLimit()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.WaitForRelease);
        var orchestrator = Orchestrator(client, clock);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent(Limit), NewTask(), Context(), Ct);
        await client.Entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit - TimeSpan.FromTicks(1));
        client.Release.SetResult();
        var result = await Bounded(run);

        Assert.True(result.Success, result.Error);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.NotNull(client.SeenDeadline);
        Assert.False(client.SeenDeadline.HasExpired);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldNeverTimeOut_WhenAgentHasNoLimitAndClockAdvancesOneHour()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.WaitForRelease);
        var orchestrator = Orchestrator(client, clock);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent(limit: null), NewTask(), Context(), Ct);
        await client.Entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(TimeSpan.FromHours(1));
        client.Release.SetResult();
        var result = await Bounded(run);

        Assert.True(result.Success, result.Error);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Null(client.SeenDeadline);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldStayCancelled_WhenCallerCancelsBeforeLimit()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.WaitForCancel);
        var orchestrator = Orchestrator(client, clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent(Limit), NewTask(), Context(), caller.Token);
        await client.Entered.Task.WaitAsync(Polling.DefaultTimeout, Ct);
        clock.Advance(Limit - TimeSpan.FromSeconds(1));
        await caller.CancelAsync();
        var result = await Bounded(run);

        Assert.Equal(AgentExitReason.Cancelled, result.ExitReason);
        Assert.NotNull(client.SeenDeadline);
        Assert.False(client.SeenDeadline.HasExpired);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldCountRateLimitWait_WhenAgentWaitsForItsMaxRpmTurn()
    {
        var clock = new ManualTimeProvider();
        using var client = new WaitingChatClient(Mode.Answer, toolCalls: 1);
        var orchestrator = Orchestrator(client, clock);

        // maxRpm 1: the second request waits 60 s for the window, past the 30 s limit.
        var run = orchestrator.ExecuteTaskCoreAsync(Agent(Limit, maxRpm: 1), NewTask(), Context(), Ct);
        await Polling.WaitUntilAsync(() => clock.PendingTimers >= 1);
        clock.Advance(Limit + TimeSpan.FromTicks(1));
        var result = await Bounded(run);

        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.ExecutionTimeExceeded, result.ExitReason);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public void ShouldPauseEnclosingDeadlines_WhenHumanWaitHappensInDelegate()
    {
        var clock = new ManualTimeProvider();
        using var delegator = TaskDeadline.Begin(Limit, clock, Ct);
        using var delegateDeadline = TaskDeadline.Begin(Limit, clock, delegator.Token);

        using (TaskDeadline.PauseCurrent())
        {
            clock.Advance(Limit * 3);
            Assert.False(delegateDeadline.HasExpired);
            Assert.False(delegator.HasExpired);
            Assert.False(delegator.Token.IsCancellationRequested);
        }

        clock.Advance(Limit + TimeSpan.FromTicks(1));

        Assert.True(delegateDeadline.HasExpired);
        Assert.True(delegator.HasExpired);
    }
}
