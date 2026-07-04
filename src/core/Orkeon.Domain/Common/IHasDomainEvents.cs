using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Common;

/// <summary>
/// Interface for entities that raise domain events.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>
    /// Gets the uncommitted domain events.
    /// </summary>
    IReadOnlyList<DomainEvent> DomainEvents { get; }

    /// <summary>
    /// Clears all domain events. This is typically called after events have been dispatched.
    /// </summary>
    void ClearDomainEvents();
}
