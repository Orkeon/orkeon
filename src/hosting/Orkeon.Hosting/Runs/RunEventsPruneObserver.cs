using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// Writes each pruning of a loop's older read-tool results on the run protocol (agent.pruned, #434). A script's
/// <c>act({ prune })</c> belongs to no task (#461): its events carry no <c>taskId</c> (null, which the wire omits).
/// </summary>
internal sealed class RunEventsPruneObserver : IAgentPruneObserver
{
    private readonly IRunEventSink _events;

    public RunEventsPruneObserver(IRunEventSink events) => _events = events;

    /// <inheritdoc />
    public void Pruned(AgentPruneNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _events.Emit(RunEventKinds.AgentPruned, new
        {
            // A script's act belongs to no task (#461): a null taskId, as a pick's agent.route (#460).
            taskId = string.IsNullOrEmpty(notice.TaskId) ? null : notice.TaskId,
            agentRole = notice.AgentRole,
            estimatedTokens = notice.EstimatedTokens,
            thresholdTokens = notice.ThresholdTokens,
            offered = notice.Offered,
            judged = notice.Judged,
            kept = notice.Kept,
            cut = notice.Cut,
            dropped = notice.Dropped,
            freedTokens = notice.FreedTokens,
        });
    }
}
