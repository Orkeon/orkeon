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
