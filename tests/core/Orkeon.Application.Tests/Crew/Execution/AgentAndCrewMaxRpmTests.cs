using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// GAP-38 — an agent's and a crew's <c>maxRpm</c> bound the model requests they make, as CrewAI's
/// <c>max_rpm</c> does: a sliding window of 60 s per object that declares a limit, the request of
/// too many waiting its turn, never failing, cancelled with its run. The host's per-agent cap
/// (<c>RateLimiting:AgentRequestsPerMinute</c>) joins the agent's window, per agent instance. On a
/// manual clock: no test waits in real time.
/// </summary>
public sealed class AgentAndCrewMaxRpmTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A chat client that answers each agent from its own script — the agent being the one the
    /// running task's usage scope names — and records, per call, the agent and the instant on the
    /// test's clock.
    /// </summary>
    private sealed class ClockedChatClient(ManualTimeProvider clock) : IChatClient
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Queue<ChatResponse>> _scripts = new(StringComparer.Ordinal);

        public List<(string Agent, TimeSpan At)> Calls { get; } = [];

        public int CallsOf(string agent)
        {
            lock (_gate)
                return Calls.Count(call => call.Agent == agent);
        }

        public TimeSpan[] InstantsOf(string agent)
        {
            lock (_gate)
                return [.. Calls.Where(call => call.Agent == agent).Select(call => call.At)];
        }

        /// <summary>The agent's next turns: <paramref name="toolCalls"/> calls of <c>lookup</c>, then its answer.</summary>
        public void Script(string agent, int toolCalls)
        {
            var script = new Queue<ChatResponse>();
            for (var i = 0; i < toolCalls; i++)
            {
                script.Enqueue(new ChatResponse([new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent($"call-{agent}-{i}", "lookup", new Dictionary<string, object?> { ["input"] = "x" })])]));
            }

            script.Enqueue(new ChatResponse([new ChatMessage(ChatRole.Assistant, $"{agent} is done")]));
            lock (_gate)
                _scripts[agent] = script;
        }

        public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var agent = LlmUsageScope.Current.AgentId;
            lock (_gate)
            {
                Calls.Add((agent, clock.Elapsed));
                var answer = _scripts.TryGetValue(agent, out var script) && script.Count > 0
                    ? script.Dequeue()
                    : new ChatResponse([new ChatMessage(ChatRole.Assistant, $"{agent} is done")]);
                return System.Threading.Tasks.Task.FromResult(answer);
            }
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A logger that keeps the lines of the run, to read the one that says who waited.</summary>
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

    /// <summary>An <see cref="ILlmRateLimiter"/> that grants every request and counts them.</summary>
    private sealed class CountingRateLimiter : ILlmRateLimiter
    {
        private int _acquired;

        public int Acquired => Volatile.Read(ref _acquired);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The lease (no-op Dispose) is owned by the returned RateLimitAcquisition; whoever acquired it disposes it.")]
        public System.Threading.Tasks.Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acquired);
            return System.Threading.Tasks.Task.FromResult(RateLimitAcquisition.Acquired(new NoLease()));
        }

        private sealed class NoLease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private static readonly SpyTool Lookup = new("lookup", result: "found");

    private static DomainAgent Agent(string role, int? maxRpm = null)
    {
        var builder = new AgentBuilder().Role(role).Goal("Answer the question").WithTool(Lookup);
        if (maxRpm is { } limit)
            builder.MaxRpm(limit);
        return builder.Build();
    }

    private static DomainCrew Crew(int? maxRpm = null)
    {
        var builder = new CrewBuilder().Goal("Answer the questions");
        if (maxRpm is { } limit)
            builder.MaxRpm(limit);
        return builder.Build();
    }

    private static DomainTask NewTask(string description = "Answer the question") =>
        DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From("An answer"));

    private static SimpleExecutionContext Context() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    private static ExecutionOrchestrator Orchestrator(
        ClockedChatClient client, ManualTimeProvider clock, int? hostAgentLimit = null, ILogger<ExecutionOrchestrator>? logger = null) =>
        new(logger ?? new LinesLogger(), new ScriptedBasicLlmProvider(), client, [Lookup], new FakeFileSystemService())
        {
            TimeProvider = clock,
            AgentRequestsPerMinute = hostAgentLimit,
        };

    /// <summary>Waits, in real time and bounded, until something waits on the test's clock.</summary>
    private static System.Threading.Tasks.Task UntilWaiting(ManualTimeProvider clock, int timers = 1) =>
        Polling.WaitUntilAsync(() => clock.PendingTimers >= timers);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    // 1 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task An_agent_with_maxRpm_2_sends_its_third_request_when_the_first_leaves_the_window()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        client.Script("Researcher", toolCalls: 2);
        var logger = new LinesLogger();
        var orchestrator = Orchestrator(client, clock, logger: logger);

        var run = orchestrator.ExecuteTaskCoreAsync(Agent("Researcher", maxRpm: 2), NewTask(), Context(), Ct);

        await UntilWaiting(clock);
        Assert.Equal([Seconds(0), Seconds(0)], client.InstantsOf("Researcher"));

        clock.Advance(Seconds(59));
        Assert.Equal(1, clock.PendingTimers);
        Assert.Equal(2, client.CallsOf("Researcher"));

        clock.Advance(Seconds(1));
        var result = await run;

        Assert.True(result.Success, result.Error);
        Assert.Equal([Seconds(0), Seconds(0), Seconds(60)], client.InstantsOf("Researcher"));
        // Who waited, how long, under which limit: one Information line.
        var line = Assert.Single(logger.Lines, l => l.StartsWith("[Information]", StringComparison.Ordinal) && l.Contains("waited", StringComparison.Ordinal));
        Assert.Contains("Researcher", line, StringComparison.Ordinal);
        Assert.Contains("60", line, StringComparison.Ordinal);
        Assert.Contains("maxRpm 2", line, StringComparison.Ordinal);
    }

    // 2 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_crew_with_maxRpm_2_makes_the_third_request_of_its_agents_wait()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        client.Script("Writer", toolCalls: 1);
        client.Script("Editor", toolCalls: 0);
        var orchestrator = Orchestrator(client, clock);
        var crew = Crew(maxRpm: 2);

        using (RequestRates.BeginRun(crew, clock))
        {
            var writer = await orchestrator.ExecuteTaskCoreAsync(Agent("Writer"), NewTask(), Context(), Ct);
            Assert.True(writer.Success, writer.Error);

            var editing = orchestrator.ExecuteTaskCoreAsync(Agent("Editor"), NewTask("Edit the answer"), Context(), Ct);
            await UntilWaiting(clock);
            Assert.Equal(0, client.CallsOf("Editor"));

            clock.Advance(Seconds(60));
            var editor = await editing;
            Assert.True(editor.Success, editor.Error);
        }

        Assert.Equal([Seconds(0), Seconds(0)], client.InstantsOf("Writer"));
        Assert.Equal([Seconds(60)], client.InstantsOf("Editor"));
    }

    // 3 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task An_agent_waiting_on_its_own_window_lets_another_agent_of_the_crew_pass()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        client.Script("Slow", toolCalls: 1);
        client.Script("Fast", toolCalls: 0);
        var orchestrator = Orchestrator(client, clock);
        var crew = Crew(maxRpm: 10);

        using (RequestRates.BeginRun(crew, clock))
        {
            var slow = orchestrator.ExecuteTaskCoreAsync(Agent("Slow", maxRpm: 1), NewTask(), Context(), Ct);
            await UntilWaiting(clock);

            var fast = await orchestrator.ExecuteTaskCoreAsync(Agent("Fast"), NewTask("Another question"), Context(), Ct);
            Assert.True(fast.Success, fast.Error);
            Assert.Equal(1, client.CallsOf("Slow"));

            clock.Advance(Seconds(60));
            Assert.True((await slow).Success);
        }

        Assert.Equal([Seconds(0), Seconds(60)], client.InstantsOf("Slow"));
        Assert.Equal([Seconds(0)], client.InstantsOf("Fast"));
    }

    // 4 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task An_agent_that_waited_for_the_crew_window_counts_its_request_when_it_went()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        client.Script("Busy", toolCalls: 1);
        client.Script("Limited", toolCalls: 1);
        var orchestrator = Orchestrator(client, clock);
        var crew = Crew(maxRpm: 2);

        using (RequestRates.BeginRun(crew, clock))
        {
            // The crew's window is full at t = 0.
            Assert.True((await orchestrator.ExecuteTaskCoreAsync(Agent("Busy"), NewTask(), Context(), Ct)).Success);

            var limited = orchestrator.ExecuteTaskCoreAsync(Agent("Limited", maxRpm: 1), NewTask("Another question"), Context(), Ct);
            await UntilWaiting(clock);
            Assert.Equal(0, client.CallsOf("Limited"));

            // Its first request goes when the crew's window has room, and counts then.
            clock.Advance(Seconds(60));
            await Polling.WaitUntilAsync(() => client.CallsOf("Limited") == 1 && clock.PendingTimers == 1);
            Assert.Equal([Seconds(60)], client.InstantsOf("Limited"));

            // Its next one waits on its own window, a minute after the first.
            clock.Advance(Seconds(59));
            Assert.Equal(1, client.CallsOf("Limited"));
            clock.Advance(Seconds(1));
            Assert.True((await limited).Success);
        }

        Assert.Equal([Seconds(60), Seconds(120)], client.InstantsOf("Limited"));
    }

    // 5 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_cancelled_wait_counts_nothing_and_delays_nothing()
    {
        var clock = new ManualTimeProvider();
        var agent = Agent("Researcher", maxRpm: 1);

        var first = await RequestRates.WaitTurnAsync(agent, hostAgentLimit: null, clock, Ct);
        Assert.Equal(TimeSpan.Zero, first.Waited);

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var waiting = RequestRates.WaitTurnAsync(agent, hostAgentLimit: null, clock, cancel.Token);
        await UntilWaiting(clock);
        clock.Advance(Seconds(10));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, clock.PendingTimers);

        // The cancelled request took no place: at t = 60 the first has left, and the next goes at once.
        clock.Advance(Seconds(50));
        var next = RequestRates.WaitTurnAsync(agent, hostAgentLimit: null, clock, Ct);
        Assert.True(next.IsCompleted);
        Assert.Equal(TimeSpan.Zero, (await next).Waited);
    }

    // 6 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task Without_maxRpm_nor_host_cap_a_hundred_requests_at_one_instant_never_wait()
    {
        var clock = new ManualTimeProvider();
        var agent = Agent("Researcher");

        using (RequestRates.BeginRun(Crew(), clock))
        {
            var turns = Enumerable.Range(0, 100)
                .Select(_ => RequestRates.WaitTurnAsync(agent, hostAgentLimit: null, clock, Ct))
                .ToList();

            Assert.All(turns, turn => Assert.True(turn.IsCompletedSuccessfully));
            Assert.All(await System.Threading.Tasks.Task.WhenAll(turns), turn => Assert.Equal(TimeSpan.Zero, turn.Waited));
        }

        Assert.Equal(0, clock.PendingTimers);
    }

    // 7 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task Outside_any_run_the_second_task_of_an_agent_with_maxRpm_1_waits()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        var orchestrator = Orchestrator(client, clock);
        var agent = Agent("Researcher", maxRpm: 1);

        Assert.True((await orchestrator.ExecuteTaskCoreAsync(agent, NewTask(), Context(), Ct)).Success);

        var second = orchestrator.ExecuteTaskCoreAsync(agent, NewTask("Another question"), Context(), Ct);
        await UntilWaiting(clock);
        Assert.Equal(1, client.CallsOf("Researcher"));

        clock.Advance(Seconds(60));
        Assert.True((await second).Success);
        Assert.Equal([Seconds(0), Seconds(60)], client.InstantsOf("Researcher"));
    }

    // 8 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task The_host_cap_bounds_each_agent_with_its_own_maxRpm_the_stricter_winning()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        client.Script("Generous", toolCalls: 2);
        client.Script("Strict", toolCalls: 1);
        var orchestrator = Orchestrator(client, clock, hostAgentLimit: 2);

        // maxRpm 5 above the host's 2: the host's cap holds, the third request waits.
        var generous = orchestrator.ExecuteTaskCoreAsync(Agent("Generous", maxRpm: 5), NewTask(), Context(), Ct);
        await UntilWaiting(clock);
        Assert.Equal(2, client.CallsOf("Generous"));

        // maxRpm 1 below it: its own holds, the second request waits.
        var strict = orchestrator.ExecuteTaskCoreAsync(Agent("Strict", maxRpm: 1), NewTask("Another question"), Context(), Ct);
        await UntilWaiting(clock, timers: 2);
        Assert.Equal(1, client.CallsOf("Strict"));

        clock.Advance(Seconds(60));
        Assert.True((await generous).Success);
        Assert.True((await strict).Success);
        Assert.Equal([Seconds(0), Seconds(0), Seconds(60)], client.InstantsOf("Generous"));
        Assert.Equal([Seconds(0), Seconds(60)], client.InstantsOf("Strict"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Two_agents_of_one_role_in_two_crews_do_not_share_the_host_cap()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        var orchestrator = Orchestrator(client, clock, hostAgentLimit: 2);

        foreach (var crew in new[] { Crew(), Crew() })
        {
            client.Script("Researcher", toolCalls: 1);
            using (RequestRates.BeginRun(crew, clock))
                Assert.True((await orchestrator.ExecuteTaskCoreAsync(Agent("Researcher"), NewTask(), Context(), Ct)).Success);
        }

        // Four requests of one role at t = 0, two per agent: nobody waited.
        Assert.Equal([Seconds(0), Seconds(0), Seconds(0), Seconds(0)], client.InstantsOf("Researcher"));
        Assert.Equal(0, clock.PendingTimers);
    }

    // 9 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task An_agent_turn_takes_no_lease_at_the_gate()
    {
        var clock = new ManualTimeProvider();
        using var client = new ClockedChatClient(clock);
        var limiter = new CountingRateLimiter();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonLlmProvider(_ => new ScriptedFullLlmProvider());
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        // The turns go to a client no entrance limited: a lease taken here would be the gate's.
        services.AddSingleton<IChatClient>(client);
        services.AddSingleton<ILlmRateLimiter>(limiter);
        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>();

        var result = await orchestrator.ExecuteTaskCoreAsync(Agent("Researcher"), NewTask(), Context(), Ct);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, client.CallsOf("Researcher"));
        Assert.Equal(0, limiter.Acquired);
    }
}
