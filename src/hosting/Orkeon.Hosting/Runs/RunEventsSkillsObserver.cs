using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>Writes the skills a task's prompt carries on the run protocol (agent.skills, #434).</summary>
internal sealed class RunEventsSkillsObserver : IAgentSkillsObserver
{
    private readonly IRunEventSink _events;

    public RunEventsSkillsObserver(IRunEventSink events) => _events = events;

    /// <inheritdoc />
    public void SkillsChosen(AgentSkillsNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _events.Emit(RunEventKinds.AgentSkills, new
        {
            taskId = notice.TaskId,
            agentRole = notice.AgentRole,
            mode = notice.Mode,
            skills = notice.Skills,
            offered = notice.Offered,
            cut = notice.Cut,
            reason = notice.Reason,
            detail = notice.Detail,
        });
    }
}
