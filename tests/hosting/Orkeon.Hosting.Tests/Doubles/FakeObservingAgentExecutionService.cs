using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// An <see cref="IAgentExecutionService"/> that tells the host's compaction and pruning observers what an agent loop
/// tells them when it compacts its context and prunes its tool results (agent.compacted, agent.pruned, #432 #434),
/// then answers the task: what a test reads to see that the host writes those events into the run's history.
/// </summary>
public sealed class FakeObservingAgentExecutionService(IAgentCompactionObserver compaction, IAgentPruneObserver pruning) : IAgentExecutionService
{
    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        var taskId = task.TaskId.ToString();
        var role = agent.Role.Value;
        compaction.Compacted(new AgentCompactionNotice
        {
            TaskId = taskId, AgentRole = role, Outcome = AgentCompactionOutcomes.Compacted,
            TokensBefore = 9_000, TokensAfter = 3_000, Window = 10_000, WindowSource = "table", KeptMessages = 4, SummaryTokens = 120,
        });
        pruning.Pruned(new AgentPruneNotice
        {
            TaskId = taskId, AgentRole = role, EstimatedTokens = 9_000, ThresholdTokens = 8_000,
            Offered = 3, Judged = 3, Kept = 1, Cut = 1, Dropped = 1, FreedTokens = 2_000,
        });
        return System.Threading.Tasks.Task.FromResult(new TaskResult(true, "observed", null, [], TimeSpan.Zero));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent, ICrewTask task, SimpleExecutionContext context, CancellationToken cancellationToken = default)
        where TOutput : class =>
        throw new NotSupportedException();
}
