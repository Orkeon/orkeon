using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// No-op <see cref="IUnitOfWork"/> for tests that need a repository but don't care about domain events.
/// </summary>
internal sealed class NullUnitOfWork : IUnitOfWork
{
    public void Track(IHasDomainEvents aggregate) { }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
