using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Routes incoming A2A task requests to the local work their skill names, and lists those
/// skills. The agent card publishes exactly what <see cref="GetSkillsAsync"/> returns, so the key
/// a peer reads on the card is the key the router compares.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IA2ATaskRouter
{
    /// <summary>
    /// The skills this router answers, as the agent card publishes them: a request whose
    /// <c>skillId</c> is not the <see cref="AgentSkill.Id"/> of one of them is refused.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The skills, in the order the card lists them.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<AgentSkill>> GetSkillsAsync(CancellationToken ct = default);

    /// <summary>
    /// Routes a task request to the work its skill names and returns the response.
    /// </summary>
    /// <param name="request">The incoming task request.</param>
    /// <param name="progress">
    /// Receives one line per step of the work as it advances — what a peer following the task
    /// with <c>sendSubscribe</c> reads, each line a <see cref="A2ATaskStatus.Working"/> update
    /// carrying it as <see cref="A2ATaskUpdate.Message"/>. Null when nobody follows the task
    /// (<c>send</c>). A router whose work has no steps reports nothing.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task response after execution.</returns>
    System.Threading.Tasks.Task<A2ATaskResponse> RouteTaskAsync(
        A2ATaskRequest request, IProgress<string>? progress, CancellationToken ct = default);
}
