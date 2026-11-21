using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A trial bench whose crew holds one decision task (#435): a pass on an on circuit asks <see cref="Model"/> one
/// question, as the crew's decision task would; a pass through the relays asks nothing. Every circuit is recorded.
/// </summary>
internal sealed class FakeDecidingTestBench : IForgeTestBench
{
    /// <summary>Builds the bench over the decision model the crew's decision task asks.</summary>
    public FakeDecidingTestBench(IDecisionModel model) => Model = model;

    /// <summary>The decision model the trial's decision task asks.</summary>
    public IDecisionModel Model { get; }

    /// <summary>The circuit of each execution, in order; null when the trial ran on the host's own circuit.</summary>
    public List<DecisionCircuit?> Circuits { get; } = [];

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(session, runNumber, null, cancellationToken);

    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(
        ForgeSession session, int runNumber, DecisionCircuit? circuit, CancellationToken cancellationToken)
    {
        Circuits.Add(circuit);
        if (circuit is not { Enabled: false })
        {
            await Model.DecideAsync(
                new DecisionRequest
                {
                    State = new JsonObject { ["demande"] = "Mon colis n'est pas arrive" },
                    Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add("urgent", new NoulQuestion()),
                },
                cancellationToken);
        }

        return new ForgeTestRun
        {
            Success = true,
            Output = "Tri des demandes.",
            TaskCount = 3,
            DurationMs = 40,
            Tokens = 500,
        };
    }
}
