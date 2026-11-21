using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.Compaction;

internal static partial class PruneLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Pruning of tool results is on, but nothing is pruned: {Reason}. The loops compact as they would without it.")]
    public static partial void CircuitOff(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: the decision model could not judge {Calls} tool result(s) ({Error}); they stay as they are.")]
    public static partial void JudgeFailed(ILogger logger, string agentRole, int calls, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentRole} task {TaskId}: {Judged} of {Offered} tool result(s) judged, {Dropped} dropped, {Cut} cut, about {FreedTokens} tokens freed.")]
    public static partial void Pruned(ILogger logger, string agentRole, string taskId, int offered, int judged, int dropped, int cut, int freedTokens);
}
