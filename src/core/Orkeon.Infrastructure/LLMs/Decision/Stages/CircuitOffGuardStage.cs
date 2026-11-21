using Orkeon.Application.Decision;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>
/// The guard of a fail-closed decision profile with the decision circuit off (#433): it cannot judge,
/// so it blocks every call before it is sent, as a failing stage of a fail-closed profile does.
/// </summary>
internal sealed class CircuitOffGuardStage : IDecisionStage
{
    private readonly DecisionVerdict _block;

    /// <summary>Initializes a new instance of <see cref="CircuitOffGuardStage"/>.</summary>
    /// <param name="reason">Why the circuit is off, as <c>DecisionCircuit.OffReason</c> says it.</param>
    public CircuitOffGuardStage(string reason) =>
        _block = DecisionVerdict.Block($"{reason}; the profile is fail-closed and its guard cannot judge");

    /// <inheritdoc />
    public string Name => "guard";

    /// <inheritdoc />
    public Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default) =>
        Task.FromResult(_block);

    /// <inheritdoc />
    public Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default) =>
        Task.FromResult(DecisionVerdict.Continue);
}
