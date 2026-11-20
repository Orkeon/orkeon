using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;

namespace Orkeon.Hosting.Runs;

/// <summary>Writes an agent loop's compaction on the run protocol (agent.compacted, #432).</summary>
internal sealed class RunEventsCompactionObserver : IAgentCompactionObserver
{
    private readonly IRunEventSink _events;

    public RunEventsCompactionObserver(IRunEventSink events) => _events = events;

    /// <inheritdoc />
    public void Compacted(AgentCompactionNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _events.Emit(RunEventKinds.AgentCompacted, new
        {
            taskId = notice.TaskId,
            agentRole = notice.AgentRole,
            outcome = notice.Outcome,
            tokensBefore = notice.TokensBefore,
            tokensAfter = notice.TokensAfter,
            window = notice.Window,
            windowSource = notice.WindowSource,
            keptMessages = notice.KeptMessages,
            summaryTokens = notice.SummaryTokens,
        });
    }
}
