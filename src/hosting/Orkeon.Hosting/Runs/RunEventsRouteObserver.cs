using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>Writes the profile a task of an agent on <c>llm.profile: auto</c> runs on, on the run protocol (agent.route, #434).</summary>
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
            taskId = notice.TaskId,
            agentRole = notice.AgentRole,
            profile = notice.Profile,
            reason = notice.Reason,
            picked = notice.Picked,
            probability = notice.Probability,
            ladder = notice.Ladder,
        });
    }
}
