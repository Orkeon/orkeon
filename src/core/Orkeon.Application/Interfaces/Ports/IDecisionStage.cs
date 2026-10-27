using Orkeon.Application.Decision;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// One decision taken around a model call by a decision profile: before the call, on the
/// conversation about to be sent; after it, on the answer. A stage judges and never rewrites:
/// its <see cref="DecisionVerdict"/> lets the call go on, blocks it, or sends it to another
/// profile. A stage that has nothing to say in a phase answers <see cref="DecisionVerdict.Continue"/>.
/// </summary>
public interface IDecisionStage
{
    /// <summary>The stage's name, in logs, audit events and a blocked call's error.</summary>
    string Name { get; }

    /// <summary>Judges the conversation before it is sent.</summary>
    /// <param name="messages">The conversation, as the caller sends it.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>Continue, Block, or Route to another profile.</returns>
    Task<DecisionVerdict> OnRequestAsync(LlmMessage[] messages, CancellationToken cancellationToken = default);

    /// <summary>Judges the answer the model gave.</summary>
    /// <param name="messages">The conversation that was sent.</param>
    /// <param name="response">The model's answer.</param>
    /// <param name="cancellationToken">Cancels the decision.</param>
    /// <returns>Continue, Block, or Escalate to another profile.</returns>
    Task<DecisionVerdict> OnResponseAsync(LlmMessage[] messages, LlmResponse response, CancellationToken cancellationToken = default);
}
