using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>The log lines of a compaction (#432).</summary>
internal static partial class CompactionLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentRole} task={TaskId}: conversation compacted, {Replaced} messages replaced by a summary ({TokensBefore} -> {TokensAfter} tokens of a {Window}-token window)")]
    internal static partial void LogCompacted(ILogger logger, string agentRole, string taskId, int replaced, int tokensBefore, int tokensAfter, int window);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole} task={TaskId}: compaction failed ({Failures} in a row): {Reason}")]
    internal static partial void LogFailed(ILogger logger, string agentRole, string taskId, int failures, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole} task={TaskId}: compaction failed {Failures} times in a row ({Reason}); the loop trims its conversation to the last 40 messages for the rest of this attempt")]
    internal static partial void LogReverted(ILogger logger, string agentRole, string taskId, int failures, string reason);
}
