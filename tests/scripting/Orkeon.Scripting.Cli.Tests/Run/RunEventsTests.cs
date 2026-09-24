using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>Records what an inner hook received, to prove the observer composes instead of replacing.</summary>
internal sealed class RecordingHook : ICrewExecutionHook
{
    /// <summary>Task ids seen, in order.</summary>
    public List<string> Tasks { get; } = [];

    /// <summary>Task ids whose start was relayed, in order.</summary>
    public List<string> Started { get; } = [];

    /// <summary>How many times the crew-completed callback fired.</summary>
    public int Completions { get; private set; }

    /// <summary>How many times the crew-failed callback fired.</summary>
    public int Failures { get; private set; }

    /// <inheritdoc />
    public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct)
    {
        Started.Add(snapshot.TaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        Tasks.Add(snapshot.TaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct)
    {
        Completions++;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot snapshot, Exception? ex, CancellationToken ct)
    {
        Failures++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The run observer (BUS-02): what a crew run puts on the shared stream, and — the part
/// that matters most — that observing a run never costs it the hook it already had.
/// <para>
/// In the CLI collection because one test runs a crew in-process, and a run writes to the
/// process-global console.
/// </para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunEventsTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private OrkeonEventWriter Writer() => new(_output, new FakeOrkeonClock());

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)),
    ];

    private static TaskExecutionSnapshot Snapshot(string taskId, string role = "Writer", bool success = true) =>
        new()
        {
            TaskId = taskId,
            AgentRole = role,
            Success = success,
            Duration = TimeSpan.FromMilliseconds(1200),
            CompletedAt = DateTimeOffset.UnixEpoch,
            ToolCallCount = 3,
            TokensUsed = 250,
        };

    private static CrewExecutionSnapshot CrewSnapshot() =>
        new()
        {
            CrewId = "c-7f3a",
            StartedAt = DateTimeOffset.UnixEpoch,
            EndedAt = DateTimeOffset.UnixEpoch,
            Tasks = ImmutableList<TaskExecutionSnapshot>.Empty,
            Status = CrewHookStatus.Completed,
        };

    private static TaskStartSnapshot Started(string taskId, string role = "Writer") =>
        new()
        {
            TaskId = taskId,
            AgentRole = role,
            StartedAt = DateTimeOffset.UnixEpoch,
        };

    [Fact]
    public async Task A_started_task_lands_on_the_stream_before_anything_is_known_about_it()
    {
        // STUDIO-17: the one moment a watcher can show as "in progress". The payload is
        // deliberately thin — no duration, no tokens, no verdict — because none exists yet.
        var inner = new RecordingHook();
        var observer = new RunEventObserver(Writer(), inner, stream: false);

        await observer.OnTaskStartedAsync(Started("collect", "Web Researcher"), TestContext.Current.CancellationToken);

        var e = Assert.Single(Events());
        Assert.Equal("task.started", e.GetProperty("kind").GetString());
        Assert.Equal("Web Researcher", e.GetProperty("agentId").GetString());   // envelope
        Assert.Equal("collect", e.GetProperty("taskId").GetString());           // payload
        Assert.Equal("Web Researcher", e.GetProperty("agentRole").GetString());
        Assert.False(e.TryGetProperty("success", out _));
        Assert.False(e.TryGetProperty("durationMs", out _));
        Assert.Equal(["collect"], inner.Started);   // relayed, like every other callback
    }

    [Fact]
    public async Task A_completed_task_lands_on_the_stream_with_its_identity_in_the_envelope()
    {
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        await observer.OnTaskCompletedAsync(Snapshot("collect", "Web Researcher"), TestContext.Current.CancellationToken);

        var e = Assert.Single(Events());
        Assert.Equal("task.completed", e.GetProperty("kind").GetString());
        Assert.Equal("Web Researcher", e.GetProperty("agentId").GetString());   // envelope
        Assert.Equal("collect", e.GetProperty("taskId").GetString());           // payload
        Assert.True(e.GetProperty("success").GetBoolean());
        Assert.Equal(1200, e.GetProperty("durationMs").GetInt64());
        Assert.Equal(250, e.GetProperty("tokens").GetInt32());
        Assert.Equal(3, e.GetProperty("toolCalls").GetInt32());
    }

    [Fact]
    public async Task A_skipped_task_says_so_on_the_wire()
    {
        // LLM-11: never started, completed with success=false, and told apart from a failure
        // by the flag — a screen shows "skipped", not a red verdict on a task nobody ran.
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);
        var skipped = Snapshot("write", "Writer", success: false) with
        {
            Skipped = true,
            SkipReason = "Task write (Writer) skipped: it depends on task score, which did not succeed",
            Duration = TimeSpan.Zero,
        };

        await observer.OnTaskCompletedAsync(skipped, TestContext.Current.CancellationToken);
        await observer.OnTaskCompletedAsync(Snapshot("collect"), TestContext.Current.CancellationToken);

        var events = Events();
        Assert.Equal(2, events.Count);
        Assert.True(events[0].GetProperty("skipped").GetBoolean());
        Assert.False(events[0].GetProperty("success").GetBoolean());
        Assert.Equal(0, events[0].GetProperty("durationMs").GetInt64());
        Assert.False(events[1].GetProperty("skipped").GetBoolean());
    }

    [Fact]
    public async Task The_observer_composes_with_the_hook_that_was_already_registered()
    {
        // The regression this test exists for: ICrewExecutionHook is a single service and
        // the runner registers AutoSummaryWriter on it when an /output mount exists.
        // Observing a run must not cost it its AUTO_SUMMARY.md.
        var inner = new RecordingHook();
        var observer = new RunEventObserver(Writer(), inner, stream: false);
        var ct = TestContext.Current.CancellationToken;

        await observer.OnTaskCompletedAsync(Snapshot("collect"), ct);
        await observer.OnCrewCompletedAsync(CrewSnapshot(), ct);
        await observer.OnCrewFailedAsync(CrewSnapshot(), new InvalidOperationException("boom"), ct);

        Assert.Equal(["collect"], inner.Tasks);
        Assert.Equal(1, inner.Completions);
        Assert.Equal(1, inner.Failures);
    }

    [Fact]
    public void The_meter_carries_both_directions_at_every_call()
    {
        // STUDIO-29: the split used to arrive with run.finished only — a watcher saw one number
        // grow and learnt what went up and what came back once the run was over.
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        observer.Record(new CostUsageEvent { CrewId = "c-7f3a", AgentId = "Writer", PromptTokens = 100, CompletionTokens = 20, Model = "m", Provider = "p" });
        observer.Record(new CostUsageEvent { PromptTokens = 30, CompletionTokens = 5, CacheHitTokens = 24, CacheMissTokens = 6 });

        var events = Events();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("cost.updated", e.GetProperty("kind").GetString()));
        Assert.Equal(120, events[0].GetProperty("tokens").GetInt64());
        Assert.Equal(100, events[0].GetProperty("promptTokens").GetInt64());
        Assert.Equal(20, events[0].GetProperty("completionTokens").GetInt64());
        Assert.Equal(155, events[1].GetProperty("tokens").GetInt64());
        Assert.Equal(130, events[1].GetProperty("promptTokens").GetInt64());
        Assert.Equal(25, events[1].GetProperty("completionTokens").GetInt64());
        Assert.Equal(155, observer.TokensUsed);

        // The cache pair only once a provider measured it — unmeasured is absent, never 0.
        Assert.False(events[0].TryGetProperty("cacheHitTokens", out _));
        Assert.False(events[0].TryGetProperty("cacheMissTokens", out _));
        Assert.Equal(24, events[1].GetProperty("cacheHitTokens").GetInt64());
        Assert.Equal(6, events[1].GetProperty("cacheMissTokens").GetInt64());

        // Identity travels in the envelope when known, and is omitted when it is not.
        Assert.Equal("c-7f3a", events[0].GetProperty("crewId").GetString());
        Assert.Equal("Writer", events[0].GetProperty("agentId").GetString());
        Assert.Equal("p", events[0].GetProperty("provider").GetString());
        Assert.False(events[1].TryGetProperty("crewId", out _));
    }

    [Fact]
    public void No_cost_reaches_the_wire_without_a_vendor_cost()
    {
        // DD-1: real cost only. A vendor that bills nothing in its answer leaves the price out
        // — no estimate from a price table, under any name.
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        observer.Record(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 20, Model = "gpt-4o", Provider = "openai" });

        var e = Assert.Single(Events());
        Assert.False(e.TryGetProperty("cost", out _));
        Assert.False(e.TryGetProperty("currency", out _));
        Assert.False(e.TryGetProperty("costSource", out _));
        Assert.False(e.TryGetProperty("usd", out _));
    }

    [Fact]
    public void The_vendor_cost_is_relayed_as_billed_and_adds_up_call_by_call()
    {
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        observer.Record(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 20, Cost = 0.0021m, CostCurrency = "USD" });
        observer.Record(new CostUsageEvent { PromptTokens = 80, CompletionTokens = 10, Cost = 0.0008m, CostCurrency = "USD" });

        var events = Events();
        Assert.Equal(0.0021m, events[0].GetProperty("cost").GetDecimal());
        Assert.Equal(0.0029m, events[1].GetProperty("cost").GetDecimal());
        Assert.All(events, e =>
        {
            Assert.Equal("USD", e.GetProperty("currency").GetString());
            Assert.Equal("vendor", e.GetProperty("costSource").GetString());
        });
    }

    [Fact]
    public void A_free_call_is_relayed_as_a_cost_of_zero()
    {
        // A free model bills 0 — which is a price, and a different fact from "no price".
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        observer.Record(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 20, Cost = 0m, CostCurrency = "USD" });

        var e = Assert.Single(Events());
        Assert.Equal(0m, e.GetProperty("cost").GetDecimal());
        Assert.Equal("USD", e.GetProperty("currency").GetString());
        Assert.Equal("vendor", e.GetProperty("costSource").GetString());
    }

    /// <summary>Two tasks, one agent: two calls to the vendor, one per task.</summary>
    private const string TwoTaskCrew =
        """
        name: billed-crew
        goal: Spend twice on a vendor that bills
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent whose vendor bills every answer."

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
          polish:
            description: "Polish it."
            expected_output: "A polished text."
            agent: writer
        """;

    /// <summary>
    /// Runs <see cref="TwoTaskCrew"/> through the real runner on <paramref name="vendor"/>, the
    /// way <c>orkeon run --events jsonl</c> wires it, and returns the meter readings — after
    /// checking the run succeeded on two calls and read its meter before it finished.
    /// </summary>
    private async Task<IReadOnlyList<JsonElement>> MeterOfTwoTaskRunAsync(FakeBillingLlmProvider vendor)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", TwoTaskCrew);
        using var console = new TestConsole(stdin: string.Empty);
        using var chatClient = new LlmProviderToChatClientAdapter(vendor);
        await using var observed = new ObservedRunContext(Writer(), stream: false, clientName: "studio");

        var exit = await RunnerExecution.RunOneShotAsync(
            RunCommand.ToRunnerOptions(new RunCommandOptions { ScriptPath = crew, AllowExternalMounts = true, Events = "jsonl" }),
            "orkeon",
            configureServices: (_, services) =>
            {
                services.Replace(ServiceDescriptor.Singleton<IBasicLlmProvider>(new LlmProviderAdapter(vendor)));
                services.Replace(ServiceDescriptor.Singleton<IChatClient>(chatClient));
                observed.WireServices(services);
            },
            TestContext.Current.CancellationToken);
        await observed.FinishAsync(exit);

        Assert.Equal(0, exit);
        Assert.Equal(2, vendor.Calls);

        var events = Events();
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();
        var meter = events.Where(e => e.GetProperty("kind").GetString() == "cost.updated").ToList();
        Assert.Equal(2, meter.Count);
        Assert.True(kinds.LastIndexOf("cost.updated") < kinds.IndexOf("run.finished"));

        // Both directions at every call, whatever the vendor bills.
        Assert.Equal([150L, 300L], meter.Select(e => e.GetProperty("tokens").GetInt64()));
        Assert.Equal([120L, 240L], meter.Select(e => e.GetProperty("promptTokens").GetInt64()));
        Assert.Equal([30L, 60L], meter.Select(e => e.GetProperty("completionTokens").GetInt64()));
        Assert.All(meter, e =>
        {
            Assert.Equal("fake-billing", e.GetProperty("provider").GetString());
            Assert.Equal("vendor/model-x", e.GetProperty("model").GetString());
            Assert.Equal("Writer", e.GetProperty("agentId").GetString());
            Assert.False(string.IsNullOrEmpty(e.GetProperty("crewId").GetString()));
            // The fake measured no cache: absent, not zero.
            Assert.False(e.TryGetProperty("cacheHitTokens", out _));
        });
        return meter;
    }

    [Fact]
    public async Task A_crew_on_a_vendor_that_bills_puts_the_split_and_the_real_cost_on_the_wire_call_by_call()
    {
        // The whole path, provider to wire, through the real runner: the charge the vendor
        // wrote in its answer crosses the chat-client adapter, the agent loop reports it with
        // the provider and the crew, and each cost.updated carries the running split and the
        // running charge — before run.finished, which is the point of a meter.
        var meter = await MeterOfTwoTaskRunAsync(new FakeBillingLlmProvider { ChargePerCall = 0.0021 });

        Assert.Equal([0.0021m, 0.0042m], meter.Select(e => e.GetProperty("cost").GetDecimal()));
        Assert.All(meter, e =>
        {
            Assert.Equal("USD", e.GetProperty("currency").GetString());
            Assert.Equal("vendor", e.GetProperty("costSource").GetString());
        });
    }

    [Fact]
    public async Task A_crew_on_a_free_model_gets_a_cost_of_zero_on_the_wire()
    {
        // The vendor billed 0: a price, relayed as such — not the silence of "no price".
        var meter = await MeterOfTwoTaskRunAsync(new FakeBillingLlmProvider { ChargePerCall = 0.0 });

        Assert.Equal([0m, 0m], meter.Select(e => e.GetProperty("cost").GetDecimal()));
        Assert.All(meter, e => Assert.Equal("vendor", e.GetProperty("costSource").GetString()));
    }

    [Fact]
    public async Task A_crew_on_a_vendor_that_bills_nothing_gets_the_split_and_no_price()
    {
        var meter = await MeterOfTwoTaskRunAsync(new FakeBillingLlmProvider { ChargePerCall = null });

        Assert.All(meter, e =>
        {
            Assert.False(e.TryGetProperty("cost", out _));
            Assert.False(e.TryGetProperty("currency", out _));
            Assert.False(e.TryGetProperty("costSource", out _));
        });
    }

    [Fact]
    public void Deltas_only_flow_when_stream_was_asked_for()
    {
        var quiet = new RunEventObserver(Writer(), inner: null, stream: false);
        quiet.OnDelta("hello");
        quiet.OnTurnCompleted();
        Assert.Empty(Events());

        using var streamed = new StringWriter();
        var loud = new RunEventObserver(new OrkeonEventWriter(streamed, new FakeOrkeonClock()), inner: null, stream: true);
        loud.OnDelta("hello");
        loud.OnDelta("");          // an empty delta is not an event
        loud.OnTurnCompleted();    // turn boundaries are already visible through task.completed

        var line = Assert.Single(streamed.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("llm.delta", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("hello", document.RootElement.GetProperty("text").GetString());
    }
}
