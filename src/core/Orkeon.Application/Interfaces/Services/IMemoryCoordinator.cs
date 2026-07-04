
using Orkeon.Domain.Memory;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces.Services
{
    /// <summary>
    /// Coordinates memory operations for agents during task execution.
    /// Abstracts memory persistence from execution logic.
    /// </summary>
    public interface IMemoryCoordinator
    {
        /// <summary>
        /// Stores task execution results in agent memory.
        /// </summary>
        System.Threading.Tasks.Task StoreTaskResultAsync(
            DomainAgent agent,
            CrewTask task,
            string output,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves relevant memories for task execution.
        /// </summary>
        System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesAsync(
            DomainAgent agent,
            CrewTask task,
            Context.SimpleExecutionContext context,
            int maxResults = 10,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores agent experience in memory.
        /// </summary>
        System.Threading.Tasks.Task StoreAgentExperienceAsync(
            DomainAgent agent,
            string experience,
            double importance,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates agent's working memory during execution.
        /// </summary>
        System.Threading.Tasks.Task UpdateWorkingMemoryAsync(
            DomainAgent agent,
            string key,
            string value,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);
    }
}
