using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A trial bench that records the decision circuit of each pass it is asked to run (#431) and answers every pass
/// with the same successful run. With an <see cref="Observer"/>, each pass also reports one model call and one
/// completed task to it, as a crew run would.
/// </summary>
internal sealed class CircuitRecordingTestBench : IForgeTestBench
{
    /// <summary>The circuit of each execution, in order; null when the trial ran on the host's own circuit.</summary>
    public List<DecisionCircuit?> Circuits { get; } = [];

    /// <summary>The tokens each pass reports.</summary>
    public long Tokens { get; init; } = 500;

    /// <summary>The duration each pass reports, in milliseconds.</summary>
    public long DurationMs { get; init; } = 40;

    /// <summary>The trial observer each pass reports its model call and its task to; null reports nothing.</summary>
    public ForgeRunObserver? Observer { get; init; }

    /// <summary>Whether the pass with the circuit off fails; every pass succeeds otherwise.</summary>
    public bool FailsTheOffPass { get; init; }

    /// <summary>What each pass does to the session while it runs (a deliverable written, say); null does nothing.</summary>
    public Action<ForgeSession>? DuringRun { get; init; }

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(session, runNumber, null, cancellationToken);

    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, DecisionCircuit? circuit, CancellationToken cancellationToken)
    {
        Circuits.Add(circuit);
        DuringRun?.Invoke(session);
        if (Observer is { } observer)
        {
            var callId = $"call-{Circuits.Count}";
            observer.Started(callId, LlmCallMode.Buffer, 10);
            observer.Finished(callId, LlmCallMode.Buffer, fellBack: false, promptTokens: 10, completionTokens: 5);
            await observer.OnTaskCompletedAsync(
                new TaskExecutionSnapshot
                {
                    TaskId = "correction",
                    AgentRole = "Correcteur",
                    Success = true,
                    Duration = TimeSpan.FromMilliseconds(DurationMs),
                    CompletedAt = DateTimeOffset.UnixEpoch,
                },
                cancellationToken);
        }

        return new ForgeTestRun
        {
            Success = !(FailsTheOffPass && circuit is { Enabled: false }),
            Output = circuit is { Enabled: false } ? "Tri par le relais." : "Tri par System One.",
            TaskCount = 3,
            DurationMs = DurationMs,
            Tokens = Tokens,
        };
    }
}
