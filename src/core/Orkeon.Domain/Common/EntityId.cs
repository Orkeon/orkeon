using System.Globalization;

namespace Orkeon.Domain.Common;

/// <summary>
/// Base class for strongly-typed entity identifiers backed by a ULID. Inherits the common
/// identifier contract from <see cref="TypedId"/> and adds Guid interop plus typed factories.
/// </summary>
/// <typeparam name="TEntityId">The concrete identity type (CRTP).</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory methods on a generic identifier type (CRTP).")]
public abstract class EntityId<TEntityId> : TypedId, IEntityId
    where TEntityId : EntityId<TEntityId>, new()
{
    /// <summary>Creates a new identifier with a generated ULID.</summary>
    public static TEntityId Create() => From(Ulid.NewUlid());

    /// <summary>Creates an identifier from an existing ULID value.</summary>
    /// <exception cref="ArgumentException">Thrown when value is empty.</exception>
    public static TEntityId From(Ulid value)
    {
        if (value == default)
            throw new ArgumentException($"Invalid {typeof(TEntityId).Name}: empty ULID", nameof(value));
        return new TEntityId { Value = value };
    }

    /// <summary>Creates an identifier from a Guid (migration helper).</summary>
    public static TEntityId From(Guid value) => From(new Ulid(value));

    /// <summary>Creates an identifier from a string representation.</summary>
    public static TEntityId Parse(string value) => From(Ulid.Parse(value, CultureInfo.InvariantCulture));

    // Equality (incl. cross-type guard) is fully provided by TypedId; no IEquatable<EntityId<T>>
    // is declared here to avoid CA1067 (a redundant equatable contract without an Equals(object) override).

    /// <summary>Implicit conversion to Guid for backward compatibility.</summary>
    public static implicit operator Guid(EntityId<TEntityId> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value.ToGuid();
    }

    /// <summary>Friendly-named alternate for the implicit conversion to <see cref="Guid"/>.</summary>
    /// <returns>The identifier value as a <see cref="Guid"/>.</returns>
    public Guid ToGuid() => Value.ToGuid();
}
