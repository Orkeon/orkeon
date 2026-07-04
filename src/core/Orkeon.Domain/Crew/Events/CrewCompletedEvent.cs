using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Crew.Events;

/// <summary>
/// Event raised when a crew completes execution.
/// </summary>
public sealed record CrewCompletedEvent : DomainEvent
{
    /// <summary>
    /// Gets the crew identifier.
    /// </summary>
    public required CrewId CrewId { get; init; }

    /// <summary>
    /// Gets the crew completion result.
    /// </summary>
    public required CrewCompletionResult Result { get; init; }

    /// <summary>Creates a <see cref="CrewCompletedEvent"/> from a <see cref="CrewOutput"/>.</summary>
    /// <param name="crewId">The crew identifier.</param>
    /// <param name="output">The crew execution output.</param>
    public static CrewCompletedEvent FromOutput(CrewId crewId, CrewOutput output)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ArgumentNullException.ThrowIfNull(output);
        return new CrewCompletedEvent
        {
            CrewId = crewId,
            Result = CrewCompletionResult.FromCrewOutput(output)
        };
    }
}
