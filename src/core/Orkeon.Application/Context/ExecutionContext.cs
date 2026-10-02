using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using RecalledMemory = Orkeon.Application.Memory.RecalledMemory;

namespace Orkeon.Application.Context;

/// <summary>
/// SimpleExecutionContext type.
/// </summary>
public record SimpleExecutionContext(
    CrewId CrewId,
    Dictionary<string, string> Variables,
    IMemoryScope Memory,
    IReadOnlyList<TaskOutput> PreviousOutputs,
    CancellationToken CancellationToken = default
)
{
    /// <summary>
    /// Whether the successful result of a task run with this context goes to the crew's memory
    /// (<see cref="IMemoryCoordinator.StoreTaskResultAsync"/>). <see langword="true"/> by default.
    /// A run whose output is not the task's result turns it off: the consensual strategy for
    /// each candidate answer, which it stores itself once the vote retained one, the hierarchical
    /// strategy for each attempt, which it stores itself once the manager accepted one (GAP-30),
    /// and <c>AgentBallotCollector</c> for each ballot (GAP-20). A crew without <c>memory: true</c>
    /// stores nothing, whatever this says.
    /// </summary>
    public bool StoreResultInMemory { get; init; } = true;

    /// <summary>
    /// Whether a task run with this context recalls the crew's memories into its prompt
    /// (<see cref="IMemoryCoordinator.RecallAsync"/>). <see langword="true"/> by default: a
    /// consensual candidate and a hierarchical attempt recall, they answer the task.
    /// <c>AgentBallotCollector</c> turns it off for each ballot, which answers a vote (GAP-30). A
    /// crew without <c>memory: true</c> recalls nothing, whatever this says.
    /// </summary>
    public bool RecallFromMemory { get; init; } = true;

    /// <summary>
    /// The crew's memories recalled for this task, which the user prompt renders after the
    /// previous outputs and before the retrieved knowledge (GAP-30). Set by
    /// <c>AgentExecutionService</c> on its own copy of the context; empty otherwise.
    /// </summary>
    public IReadOnlyList<RecalledMemory> RecalledMemories { get; init; } = [];

    /// <summary>
    /// Create.
    /// </summary>
    public static SimpleExecutionContext Create(CrewId crewId, CrewInput input, IMemoryScope memory)
    {
        ArgumentNullException.ThrowIfNull(input);
        var stringVariables = input.GetStringVariables() is { } vars
            ? new Dictionary<string, string>(vars)
            : [];

        return new SimpleExecutionContext(
            crewId,
            stringVariables,
            memory,
            [],
            CancellationToken.None
        );
    }
}
