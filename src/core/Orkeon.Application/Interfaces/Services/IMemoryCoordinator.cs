using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces.Services
{
    /// <summary>
    /// The crew's memory as a run uses it (GAP-30): checked before the first task, recalled before
    /// each task, written after each task that succeeds — for a crew with <c>memory: true</c> only.
    /// A memory that fails during the run is a warning, never a failed task: the coordinator logs
    /// it and the task keeps its output.
    /// </summary>
    public interface IMemoryCoordinator
    {
        /// <summary>
        /// Refuses, before the first LLM call, a crew with <c>memory: true</c> whose memory cannot
        /// work: it embeds a probe text, then searches the crew's memory once. An embedder that is
        /// missing or refuses, a store that cannot be reached, or a vector the store refuses fails
        /// with an <see cref="InvalidOperationException"/> naming the crew and the cause. A crew
        /// without memory passes untouched.
        /// </summary>
        /// <param name="crewId">The crew, recorded at kickoff.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        System.Threading.Tasks.Task EnsureReadyAsync(CrewId crewId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The crew's memories closest to <paramref name="task"/>, for its prompt: bounded by
        /// <see cref="CrewMemoryOptions"/>, without the ones the context already carries as previous
        /// outputs. Empty for a crew without memory, and when the recall fails (logged as a warning).
        /// </summary>
        System.Threading.Tasks.Task<IReadOnlyList<RecalledMemory>> RecallAsync(
            DomainAgent agent,
            CrewTask task,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores <paramref name="output"/>, the result of <paramref name="task"/>, in the crew's
        /// memory, embedded on the task and its output. Does nothing for a crew without memory; a
        /// store that fails is logged as a warning.
        /// </summary>
        System.Threading.Tasks.Task StoreTaskResultAsync(
            DomainAgent agent,
            CrewTask task,
            string output,
            Context.SimpleExecutionContext context,
            CancellationToken cancellationToken = default);
    }
}
