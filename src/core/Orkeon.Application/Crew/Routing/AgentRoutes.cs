using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// The host profiles an agent's own calls can run on in a crew (#434): what a check made before the run, such as an
/// offline run's, must resolve. An agent on <c>llm.profile: auto</c> runs on the default profile or on a rung of the
/// ladder, unless it is never routed: in a crew on <c>decision.mode: none</c>, or as a manager or a decision relay
/// (<c>without.agent</c>) with no task of its own, it runs on the default profile alone.
/// </summary>
public static class AgentRoutes
{
    /// <summary>The host profiles the calls of <paramref name="agent"/> can run on, each once, the default first.</summary>
    /// <param name="crew">The crew the agent runs in.</param>
    /// <param name="agent">The agent; one that carries its own provider runs on no host profile.</param>
    /// <param name="tasks">The crew's tasks.</param>
    /// <param name="ladder">The host's ladder (<see cref="AgentRouteOptions.Ladder"/>).</param>
    /// <returns>The profile names: the agent's own, <see cref="LlmProfiles.Default"/> for none.</returns>
    public static IReadOnlyList<string> ProfilesOf(DomainCrew crew, DomainAgent agent, IReadOnlyList<DomainTask> tasks, IEnumerable<string> ladder)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(ladder);
        var profile = agent.LlmConfig?.Profile;
        if (string.IsNullOrWhiteSpace(profile) || LlmProfiles.IsDefault(profile))
            return [LlmProfiles.Default];
        if (!LlmProfiles.IsAuto(profile))
            return [profile.Trim()];
        if (!IsRouted(crew, agent, tasks))
            return [LlmProfiles.Default];

        return [.. new[] { LlmProfiles.Default }
            .Concat(ladder.Select(rung => rung.Trim()).Where(rung => rung.Length > 0 && !LlmProfiles.IsDefault(rung)))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Whether System One routes the tasks of <paramref name="agent"/>: not in a crew on <c>decision.mode: none</c>,
    /// nor for a manager or a relay no task is assigned to, as the crew load judges it.
    /// </summary>
    private static bool IsRouted(DomainCrew crew, DomainAgent agent, IReadOnlyList<DomainTask> tasks)
    {
        if (crew.DecisionMode == CrewDecisionMode.None)
            return false;
        if (tasks.Any(t => t.AssignedAgent == agent.Id))
            return true;

        var manager = crew.ManagerAgentId == agent.Id;
        var relay = tasks.Any(t => t.Decision?.Without is { } without
            && (without.AgentId is { } id ? id == agent.Id : string.Equals(without.Agent, agent.Role.Value, StringComparison.Ordinal)));
        return !(manager || relay);
    }
}
