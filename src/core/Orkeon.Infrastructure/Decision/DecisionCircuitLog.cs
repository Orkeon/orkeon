using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Decision;

/// <summary>
/// The one warning each consumer of the decision model says when the decision circuit is off and it
/// degrades (#433). Each is said once: by the singleton that builds the consumer.
/// </summary>
internal static partial class DecisionCircuitLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The LLM judge (Orkeon:Guardian:Llm) is left out of the guardian pipeline: {Reason}. FailMode is Open: the built-in guards stay.")]
    public static partial void JudgeLeftOut(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Decision profile {Profile} takes no decision: {Reason}. Its calls go to '{To}', with no guard and no escalation.")]
    public static partial void ProfilePassesThrough(ILogger logger, string profile, string reason, string to);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Decision profile {Profile} takes no decision: {Reason}. The profile is fail-closed and holds a guard that cannot judge: every call through it is blocked.")]
    public static partial void ProfileBlocksEveryCall(ILogger logger, string profile, string reason);
}
