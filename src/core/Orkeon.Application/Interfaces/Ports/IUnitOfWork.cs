using Orkeon.Domain.Common;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Unit of Work pattern interface for coordinating persistence across repositories.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Registers an aggregate root for tracking so its domain events
    /// will be dispatched when <see cref="SaveChangesAsync"/> is called.
    /// Only one aggregate may be tracked per unit-of-work scope (one command = one aggregate).
    /// </summary>
    /// <param name="aggregate">The aggregate root to track.</param>
    void Track(IHasDomainEvents aggregate);

    /// <summary>
    /// Saves all pending changes across repositories.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    System.Threading.Tasks.Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
