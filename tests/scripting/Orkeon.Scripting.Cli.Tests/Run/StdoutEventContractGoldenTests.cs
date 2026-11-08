using Orkeon.Scripting.Cli.Tests.Doubles;
using Orkeon.Application.Runs;
using Orkeon.Hosting.Runs;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// FWK-08: the stdout JSONL contract of <c>orkeon run --events jsonl</c> is pinned byte for byte, from
/// the first line to the last, whatever sinks the events also reach. The golden was captured from the
/// writer that preceded the sink port: a drift here is a break of the wire contract Studio reads.
/// </summary>
public sealed class StdoutEventContractGoldenTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private const string RunId = "01K9FWK8000000000000000000";

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task TheStreamOfARunIsByteIdentical_ToTheGolden()
    {
        var scope = new AsyncLocalRunScope();
        var events = RunEventStreams.Stdout(_output, scope, new FakeRunTimeProvider());
        var time = new ManualTimeProvider();
        using (scope.Enter(new RunScope(ExecutionId.From(RunId), "crew.yaml")))
        {
            events.Emit(RunEventKinds.RunStarted, new { target = "crew.yaml", stream = true });

            var observer = new RunEventObserver(events, inner: null, stream: true);
            await observer.OnTaskStartedAsync(
                new TaskStartSnapshot { TaskId = "t1", AgentRole = "Rédacteur", StartedAt = DateTimeOffset.UnixEpoch },
                CancellationToken.None);
            observer.OnDelta("Bonjour — à bientôt");
            observer.Record(new CostUsageEvent { PromptTokens = 10, CompletionTokens = 5, Model = "m1", Provider = "p1", CrewId = "c-1", AgentId = "a-1" });
            new RunEventsLlmCallObserver(events, time).Started("c1", Orkeon.Domain.SharedKernel.ValueObjects.LlmCallMode.Stream, 7);
            await observer.OnTaskCompletedAsync(
                new TaskExecutionSnapshot
                {
                    TaskId = "t1",
                    AgentRole = "Rédacteur",
                    Success = true,
                    Duration = TimeSpan.FromMilliseconds(1200),
                    CompletedAt = DateTimeOffset.UnixEpoch,
                    ToolCallCount = 2,
                    TokensUsed = 15,
                },
                CancellationToken.None);
            events.Emit(
                RunEventKinds.Error,
                new RunEventScope { CorrelationId = "msg-7", CausationId = "ev-1" },
                new { code = "crew_failed", message = "boom", recoverable = false, seq = 99, skipped = (string?)null });
            events.Emit(RunEventKinds.RunFinished, new { success = false, exitCode = 1, tokens = 15, durationMs = 5L });
        }

        Assert.Equal(Golden, _output.ToString().TrimEnd('\n'));
    }

    private const string Golden = """
        {"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"run.started","correlationId":"01K9FWK8000000000000000000","target":"crew.yaml","stream":true}
        {"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"task.started","agentId":"Rédacteur","correlationId":"01K9FWK8000000000000000000","taskId":"t1","agentRole":"Rédacteur","attempt":1}
        {"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"llm.delta","correlationId":"01K9FWK8000000000000000000","text":"Bonjour — à bientôt"}
        {"v":2,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"cost.updated","crewId":"c-1","agentId":"a-1","correlationId":"01K9FWK8000000000000000000","tokens":15,"promptTokens":10,"completionTokens":5,"model":"m1","provider":"p1"}
        {"v":2,"seq":5,"ts":"2026-08-19T12:00:04Z","kind":"llm.call.started","correlationId":"01K9FWK8000000000000000000","callId":"c1","mode":"stream","estimatedPromptTokens":7}
        {"v":2,"seq":6,"ts":"2026-08-19T12:00:05Z","kind":"task.completed","agentId":"Rédacteur","correlationId":"01K9FWK8000000000000000000","taskId":"t1","agentRole":"Rédacteur","success":true,"skipped":false,"resumed":false,"attempt":1,"durationMs":1200,"tokens":15,"toolCalls":2,"defaulted":false,"isDecision":false}
        {"v":2,"seq":7,"ts":"2026-08-19T12:00:06Z","kind":"error","correlationId":"msg-7","causationId":"ev-1","code":"crew_failed","message":"boom","recoverable":false}
        {"v":2,"seq":8,"ts":"2026-08-19T12:00:07Z","kind":"run.finished","correlationId":"01K9FWK8000000000000000000","success":false,"exitCode":1,"tokens":15,"durationMs":5}
        """;
}
