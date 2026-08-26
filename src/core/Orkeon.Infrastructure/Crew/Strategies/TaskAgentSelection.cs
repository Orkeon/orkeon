using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Which agent runs a task: the one the crew declared, and round-robin only when it declared
/// none (or named an agent the crew no longer carries).
/// <para>
/// One implementation, because there were three and one of them was wrong. Sequential and
/// Graph each held a private <c>SelectAgent</c> with this logic; <c>ParallelProcessStrategy</c>
/// wrote <c>agents[taskIndex % agents.Count]</c> inline and so ignored <c>agent:</c> entirely —
/// a YAML crew's explicit assignment was silently replaced by loop order in that one mode.
/// </para>
/// </summary>
internal static class TaskAgentSelection
{
    /// <summary>
    /// The agent for <paramref name="task"/>. <paramref name="fallbackIndex"/> is the
    /// round-robin cursor: it advances on every call, declared or not, so a crew mixing
    /// assigned and unassigned tasks spreads the unassigned ones the way it always did.
    /// </summary>
    public static DomainAgent ForTask(
        Orkeon.Domain.Task.CrewTask task,
        IReadOnlyList<DomainAgent> agents,
        ref int fallbackIndex)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count == 0)
            throw new ArgumentException("A crew must carry at least one agent to run a task.", nameof(agents));

        var agent = ForTask(task, agents, fallbackIndex);
        fallbackIndex++;
        return agent;
    }

    /// <summary>
    /// The same choice without advancing a cursor — for callers that already hold the index
    /// of the task they are placing (the graph walks its own order).
    /// </summary>
    public static DomainAgent ForTask(
        Orkeon.Domain.Task.CrewTask task,
        IReadOnlyList<DomainAgent> agents,
        int fallbackIndex)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count == 0)
            throw new ArgumentException("A crew must carry at least one agent to run a task.", nameof(agents));

        return task.AssignedAgent is { } assigned
            ? agents.FirstOrDefault(a => a.Id == assigned) ?? agents[fallbackIndex % agents.Count]
            : agents[fallbackIndex % agents.Count];
    }
}
