using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>Writes each pruning of a loop's older read-tool results on the run protocol (agent.pruned, #434).</summary>
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
            taskId = notice.TaskId,
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
