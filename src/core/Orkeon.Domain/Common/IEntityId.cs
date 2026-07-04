namespace Orkeon.Domain.Common;

/// <summary>
/// Marker interface for all strongly-typed entity identifiers.
/// </summary>
public interface IEntityId
{
    /// <summary>Gets the underlying ULID value.</summary>
    Ulid Value { get; }
}
