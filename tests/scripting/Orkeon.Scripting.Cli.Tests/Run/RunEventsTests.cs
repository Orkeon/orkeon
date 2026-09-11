using System.Collections.Immutable;
using System.Text.Json;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>Records what an inner hook received, to prove the observer composes instead of replacing.</summary>
internal sealed class RecordingHook : ICrewExecutionHook
{
    /// <summary>Task ids seen, in order.</summary>
    public List<string> Tasks { get; } = [];

    /// <summary>How many times the crew-completed callback fired.</summary>
    public int Completions { get; private set; }

    /// <summary>How many times the crew-failed callback fired.</summary>
    public int Failures { get; private set; }

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
/// </summary>
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
    public void The_meter_accumulates_and_never_invents_a_price()
    {
        var observer = new RunEventObserver(Writer(), inner: null, stream: false);

        observer.Record(new CostUsageEvent { CrewId = "c-7f3a", AgentId = "a-91b", PromptTokens = 100, CompletionTokens = 20, Model = "m", Provider = "p" });
        observer.Record(new CostUsageEvent { PromptTokens = 30, CompletionTokens = 5 });

        var events = Events();
        Assert.Equal(2, events.Count);
        Assert.Equal(120, events[0].GetProperty("tokens").GetInt64());
        Assert.Equal(155, events[1].GetProperty("tokens").GetInt64());
        Assert.Equal(155, observer.TokensUsed);

        // Identity travels in the envelope when known, and is omitted when it is not.
        Assert.Equal("c-7f3a", events[0].GetProperty("crewId").GetString());
        Assert.False(events[1].TryGetProperty("crewId", out _));

        // No currency anywhere: the framework has no price table, so the stream has no price.
        Assert.All(events, e => Assert.False(e.TryGetProperty("usd", out _)));
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
