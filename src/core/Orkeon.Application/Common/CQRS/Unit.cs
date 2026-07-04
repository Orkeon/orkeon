namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Represents a void type for commands that don't return a value.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>
    /// Gets the single Unit value.
    /// </summary>
    public static readonly Unit Value;

    /// <summary>
    /// Determines whether this unit is equal to another unit.
    /// </summary>
    public bool Equals(Unit other) => true;

    /// <summary>
    /// Determines whether this unit is equal to the specified object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>
    /// Returns the hash code for this unit.
    /// </summary>
    public override int GetHashCode() => 0;

    /// <summary>
    /// Returns a string representation of the unit.
    /// </summary>
    public override string ToString() => "()";

    /// <summary>
    /// Determines whether two unit values are equal.
    /// </summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>
    /// Determines whether two unit values are not equal.
    /// </summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
