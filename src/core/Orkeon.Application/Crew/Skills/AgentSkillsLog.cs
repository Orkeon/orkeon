using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.Skills;

internal static partial class AgentSkillsLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent skills with auto take no pick: {Reason}. Each such agent uses its fallback list, or no skill when it has none.")]
    public static partial void CircuitOff(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent skills with auto take no pick: {Reason}. Each such agent uses its fallback list, or no skill when it has none.")]
    public static partial void EmptyLibrary(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: the decision model could not pick its skills ({Error}); its fallback list stands.")]
    public static partial void PickFailed(ILogger logger, string agentRole, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Agent {AgentRole}: the skill {Path} could not be read or is empty; it is left out of the prompt.")]
    public static partial void SkillLeftOut(ILogger logger, string agentRole, string path);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent {AgentRole} task {TaskId}: skills ({Mode}) {Skills}.")]
    public static partial void Chosen(ILogger logger, string agentRole, string taskId, string mode, string skills);
}
