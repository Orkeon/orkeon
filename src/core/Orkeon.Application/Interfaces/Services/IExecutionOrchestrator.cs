using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces.Services
{
    /// <summary>
    /// Core execution orchestration interface focused solely on task execution flow.
    /// </summary>
    public interface IExecutionOrchestrator
    {
        /// <summary>
        /// Executes a task for an agent without side effects.
        /// </summary>
        System.Threading.Tasks.Task<TaskResult> ExecuteTaskCoreAsync(
            DomainAgent agent,
            CrewTask task,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);
    }
}
