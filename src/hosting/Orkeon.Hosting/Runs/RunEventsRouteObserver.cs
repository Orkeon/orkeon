using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>
/// Writes the profile a task of an agent on <c>llm.profile: auto</c> runs on (agent.route, #434) and the thinking level
/// a task on <c>effort: auto</c> runs with (agent.effort, #439), on the run protocol. A script's <c>ctx.llm.pick</c>
/// belongs to no task (#460): its events carry a null <c>taskId</c>.
/// </summary>
internal sealed class RunEventsRouteObserver : IAgentRouteObserver
{
    private readonly IRunEventSink _events;

    public RunEventsRouteObserver(IRunEventSink events) => _events = events;

    /// <inheritdoc />
    public void ProfileRouted(AgentRouteNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _events.Emit(RunEventKinds.AgentRoute, new
        {
            taskId = string.IsNullOrEmpty(notice.TaskId) ? null : notice.TaskId,
            agentRole = notice.AgentRole,
            profile = notice.Profile,
            reason = notice.Reason,
            picked = notice.Picked,
            probability = notice.Probability,
            ladder = notice.Ladder,
        });
    }

    /// <inheritdoc />
    public void EffortPicked(AgentEffortNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _events.Emit(RunEventKinds.AgentEffort, new
        {
            taskId = string.IsNullOrEmpty(notice.TaskId) ? null : notice.TaskId,
            agentRole = notice.AgentRole,
            level = notice.Level,
            previous = notice.Previous,
            reason = notice.Reason,
            picked = notice.Picked,
            probability = notice.Probability,
            levels = notice.Levels,
        });
    }
}
