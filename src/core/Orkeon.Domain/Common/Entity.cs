namespace Orkeon.Domain.Common;

/// <summary>
/// Base class for all domain entities with a strongly-typed identifier.
/// </summary>
/// <typeparam name="TEntityId">The entity's identifier type.</typeparam>
public abstract class Entity<TEntityId>
    where TEntityId : EntityId<TEntityId>, new()
{
    /// <summary>Gets the entity's unique identifier.</summary>
    public TEntityId Id { get; }

    /// <summary>
    /// Gets the timestamp when this entity was created.
    /// </summary>
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the timestamp when this entity was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; protected set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the version number for optimistic concurrency control.
    /// </summary>
    public int Version { get; protected set; } = 1;

    /// <summary>Initializes a new entity with the given identifier.</summary>
    protected Entity(TEntityId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    /// <summary>Initializes a new entity with a default identifier.</summary>
    protected Entity() { Id = default!; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TEntityId> other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name} [Id={Id}]";
}
