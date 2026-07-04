using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Recording <see cref="IUnitOfWork"/> double: exposes the aggregates passed to
/// <see cref="Track"/> so tests can assert domain-event tracking behaviour.
/// </summary>
internal sealed class MockUnitOfWork : IUnitOfWork
{
    public List<IHasDomainEvents> TrackedAggregates { get; } = [];

    public int SaveChangesCallCount { get; private set; }

    public void Track(IHasDomainEvents aggregate) => TrackedAggregates.Add(aggregate);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(0);
    }
}
