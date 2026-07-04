namespace Orkeon.Domain.Common;

/// <summary>
/// Common abstract base for all strongly-typed identifiers in Orkeon. Encapsulates a
/// <see cref="Ulid"/> <see cref="Value"/> from which equality, hashing,
/// <see cref="ToString"/>, the <c>==</c>/<c>!=</c> operators and the implicit conversion
/// to <see cref="string"/> all derive.
/// <para>
/// Cross-type equality is forbidden by construction: two <see cref="TypedId"/> instances
/// of different runtime types are never equal, even when their <see cref="Value"/> coincide.
/// </para>
/// </summary>
// S4035: this abstract base cannot be sealed (it is the root of the typed-id hierarchy).
// The Equals(TypedId?) implementation guards with GetType() == other.GetType(), so derived
// types of different runtime types are never considered equal — the equality contract is
// honoured across the hierarchy.
#pragma warning disable S4035
public abstract class TypedId : IEquatable<TypedId>
#pragma warning restore S4035
{
    /// <summary>Gets the underlying ULID value.</summary>
    public Ulid Value { get; protected init; }

    /// <summary>Parameterless constructor for derived types that set <see cref="Value"/> via object initializer.</summary>
    protected TypedId() { }

    /// <summary>Initializes the identifier with the supplied ULID value.</summary>
    protected TypedId(Ulid value) => Value = value;

    /// <summary>Returns the canonical string form (26-char Crockford base32 by default).</summary>
    public virtual string AsString() => Value.ToString();

    /// <inheritdoc/>
    public override string ToString() => AsString();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TypedId other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(TypedId? other) =>
        other is not null
        && GetType() == other.GetType()
        && Value == other.Value;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(GetType(), Value);

    /// <summary>Determines whether two typed identifiers are equal.</summary>
    public static bool operator ==(TypedId? left, TypedId? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two typed identifiers are not equal.</summary>
    public static bool operator !=(TypedId? left, TypedId? right) => !(left == right);

    /// <summary>Implicit conversion to <see cref="string"/> using <see cref="AsString"/>.</summary>
    public static implicit operator string(TypedId? id) => id?.AsString() ?? string.Empty;
}
