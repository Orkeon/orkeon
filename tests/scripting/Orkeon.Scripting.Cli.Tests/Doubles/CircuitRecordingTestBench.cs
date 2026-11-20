using Orkeon.Application.Crew.Execution;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A trial bench that records the decision circuit of each pass it is asked to run (#431) and answers every pass
/// with the same successful run.
/// </summary>
internal sealed class CircuitRecordingTestBench : IForgeTestBench
{
    /// <summary>The circuit of each execution, in order; null when the trial ran on the host's own circuit.</summary>
    public List<DecisionCircuit?> Circuits { get; } = [];

    /// <summary>The tokens each pass reports.</summary>
    public long Tokens { get; init; } = 500;

    /// <summary>The duration each pass reports, in milliseconds.</summary>
    public long DurationMs { get; init; } = 40;

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(session, runNumber, null, cancellationToken);

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, DecisionCircuit? circuit, CancellationToken cancellationToken)
    {
        Circuits.Add(circuit);
        return Task.FromResult(new ForgeTestRun
        {
            Success = true,
            Output = circuit is { Enabled: false } ? "Tri par le relais." : "Tri par System One.",
            TaskCount = 3,
            DurationMs = DurationMs,
            Tokens = Tokens,
        });
    }
}
