using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.Routing;

internal static partial class AgentRouteLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agents on llm.profile: auto take no pick: {Reason}. Each of their tasks runs on the default profile.")]
    public static partial void CircuitOff(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole} is on llm.profile: auto, and this host declares no {Key}: its tasks run on the default profile.")]
    public static partial void NoLadder(ILogger logger, string agentRole, string key);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: the decision model could not pick its profile ({Error}); the default profile stands.")]
    public static partial void PickFailed(ILogger logger, string agentRole, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentRole} task {TaskId}: profile {Profile} ({Reason}).")]
    public static partial void Routed(ILogger logger, string agentRole, string taskId, string profile, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agents and tasks on thinking: {{ effort: auto }} take no pick: {Reason}. Each of their tasks runs with the effort it would have had without auto.")]
    public static partial void EffortCircuitOff(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: the decision model could not pick its thinking effort ({Error}); the effort it would have had without auto stands.")]
    public static partial void EffortPickFailed(ILogger logger, string agentRole, string error);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentRole} task {TaskId}: thinking effort {Level} ({Reason}).")]
    public static partial void EffortPicked(ILogger logger, string agentRole, string taskId, string level, string reason);
}
