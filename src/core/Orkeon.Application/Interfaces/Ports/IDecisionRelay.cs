using Orkeon.Application.Decision.DTOs;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// The <c>without:</c> relay of a decision task (#430): one LLM call, on the provider of the crew agent the
/// relay names, that answers the task's questions with probabilities only. It runs in an <c>optional</c> crew
/// when the decision circuit is off, in place of the <see cref="IDecisionModel"/>; it has no tool and no memory.
/// The caller builds the prompt and the JSON schema and checks the answer; an implementation picks how the
/// schema travels (<c>json_schema</c> when the provider declares it, the prompt otherwise) and meters the call
/// as <see cref="LlmUsageOperations.Relay"/>, under the agent's role.
/// </summary>
public interface IDecisionRelay
{
    /// <summary>Asks the relay agent once.</summary>
    /// <param name="request">The agent, the prompts and the schema.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text it answered and the tokens it used.</returns>
    Task<DecisionRelayResponse> AskAsync(DecisionRelayRequest request, CancellationToken cancellationToken = default);
}
