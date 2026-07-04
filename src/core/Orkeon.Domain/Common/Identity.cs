namespace Orkeon.Domain.Common;

/// <summary>
/// Base class for strongly-typed identifiers in the domain using GUID values.
/// </summary>
/// <typeparam name="T">The concrete identity type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory methods on a generic identity type (CRTP).")]
public abstract record Identity<T>(Guid Value) where T : Identity<T>
{
    /// <summary>
    /// Creates a new instance of the identity with a generated unique value.
    /// </summary>
    protected Identity() : this(Guid.NewGuid()) { }

    /// <summary>
    /// Creates a new instance of the identity with a generated unique value.
    /// </summary>
    public static T Create() => Activator.CreateInstance<T>();

    /// <summary>
    /// Creates an identity from an existing GUID value.
    /// </summary>
    /// <param name="value">The GUID value to use as the identity.</param>
    /// <returns>A new instance of the identity.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public static T From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"Invalid {typeof(T).Name}: empty GUID", nameof(value));

        return (T)Activator.CreateInstance(typeof(T), value)!;
    }

    /// <summary>
    /// Returns the string representation of this identity.
    /// </summary>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Implicit conversion to GUID for compatibility.
    /// </summary>
    public static implicit operator Guid(Identity<T> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value;
    }

    /// <summary>Friendly-named alternate for the implicit conversion to <see cref="Guid"/>.</summary>
    /// <returns>The identity value as a <see cref="Guid"/>.</returns>
    public Guid ToGuid() => Value;

    /// <summary>
    /// Implicit conversion to string for compatibility.
    /// </summary>
    public static implicit operator string(Identity<T> id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Value.ToString();
    }
}
