using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

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
    /// each candidate answer, which it stores itself once the vote retained one, and
    /// <c>AgentBallotCollector</c> for each ballot (GAP-20).
    /// </summary>
    public bool StoreResultInMemory { get; init; } = true;

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
