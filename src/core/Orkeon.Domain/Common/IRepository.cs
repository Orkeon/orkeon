namespace Orkeon.Domain.Common;

/// <summary>
/// Generic repository interface for aggregate roots.
/// Phase 3.1.3: Type-safe repository pattern with complete generics.
/// </summary>
public interface IRepository<TAggregate, TId>
    where TAggregate : class, IHasDomainEvents
    where TId : notnull
{
    /// <summary>
    /// Retrieves an aggregate by its identifier.
    /// </summary>
    System.Threading.Tasks.Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new aggregate to the repository.
    /// </summary>
    System.Threading.Tasks.Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing aggregate in the repository.
    /// </summary>
    System.Threading.Tasks.Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an aggregate from the repository.
    /// </summary>
    System.Threading.Tasks.Task DeleteAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an aggregate with the specified identifier exists.
    /// </summary>
    System.Threading.Tasks.Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of aggregates.
    /// </summary>
    System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Specification pattern interface for type-safe queries.
/// </summary>
public interface ISpecification<T>
{
    /// <summary>Determines whether the specification is satisfied by the given candidate.</summary>
    /// <param name="candidate">The candidate to evaluate.</param>
    /// <returns><see langword="true"/> if satisfied; otherwise <see langword="false"/>.</returns>
    bool IsSatisfiedBy(T candidate);
    /// <summary>Combines this specification with another using logical AND.</summary>
    /// <param name="other">The other specification.</param>
    /// <returns>A combined specification.</returns>
    ISpecification<T> AndWith(ISpecification<T> other);
    /// <summary>Combines this specification with another using logical OR.</summary>
    /// <param name="other">The other specification.</param>
    /// <returns>A combined specification.</returns>
    ISpecification<T> OrWith(ISpecification<T> other);
    /// <summary>Negates this specification.</summary>
    /// <returns>A negated specification.</returns>
    ISpecification<T> Negate();
}

/// <summary>
/// Repository interface with specification support.
/// </summary>
public interface ISpecificationRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : class, IHasDomainEvents
    where TId : notnull
{
    /// <summary>
    /// Finds aggregates matching a specification.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<TAggregate>> FindAsync(
        ISpecification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds aggregates matching a specification with pagination.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<TAggregate>> FindAsync(
        ISpecification<TAggregate> specification,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts aggregates matching a specification.
    /// </summary>
    System.Threading.Tasks.Task<int> CountAsync(
        ISpecification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any aggregate matches a specification.
    /// </summary>
    System.Threading.Tasks.Task<bool> AnyAsync(
        ISpecification<TAggregate> specification,
        CancellationToken cancellationToken = default);
}
