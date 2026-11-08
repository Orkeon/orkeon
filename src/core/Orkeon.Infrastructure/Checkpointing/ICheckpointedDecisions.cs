using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// What a strategy that runs a decision task itself (a decision runs no agent, so it never reaches
/// <see cref="IAgentExecutionService"/>) asks of the checkpointing decorator: the decision is taken once,
/// kept in the run's checkpoint under the task's invocation key (<c>&lt;task&gt;#&lt;n&gt;</c>) and replayed,
/// not asked again, when the run is resumed. A decision asked again could answer otherwise and send the
/// resumed run down another route than the interrupted one (FWK-28).
/// </summary>
internal interface ICheckpointedDecisions
{
    /// <summary>
    /// Answers <paramref name="task"/> from the run's checkpoint when it holds the decision of this invocation,
    /// else runs <paramref name="decide"/> and checkpoints a successful answer.
    /// </summary>
    /// <param name="task">The decision task.</param>
    /// <param name="decide">Takes the decision when the checkpoint holds none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    System.Threading.Tasks.Task<TaskResult> RunDecisionAsync(
        Orkeon.Domain.Task.ICrewTask task,
        Func<System.Threading.Tasks.Task<TaskResult>> decide,
        CancellationToken cancellationToken);
}
