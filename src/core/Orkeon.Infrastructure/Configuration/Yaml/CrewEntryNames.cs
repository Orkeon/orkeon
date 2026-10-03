using Orkeon.Domain.Configuration;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// The name an agent or a task goes by in a crew file and in a message about it (GAP-39): its key,
/// the one its author wrote and the YAML loader keeps — else its identifier, for a configuration
/// built in code or by a <c>.ork.ts</c> script, which has no key.
/// </summary>
internal static class CrewEntryNames
{
    /// <summary>The agent's key, else its identifier.</summary>
    public static string Of(AgentConfiguration agent) =>
        string.IsNullOrWhiteSpace(agent.Key) ? agent.Id.ToString() : agent.Key;

    /// <summary>The task's key, else its identifier.</summary>
    public static string Of(TaskConfiguration task) =>
        string.IsNullOrWhiteSpace(task.Key) ? task.Id.ToString() : task.Key;
}
