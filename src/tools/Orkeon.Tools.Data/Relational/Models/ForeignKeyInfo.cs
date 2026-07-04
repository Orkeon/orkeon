namespace Orkeon.Tools.Data.Relational.Models;

/// <summary>
/// Represents metadata about a foreign key constraint.
/// </summary>
public sealed record ForeignKeyInfo
{
    /// <summary>Gets the constraint name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Gets the referencing (source) table name.</summary>
    public string SourceTable { get; init; } = "";

    /// <summary>Gets the referencing (source) column names.</summary>
    public IReadOnlyList<string> SourceColumns { get; init; } = [];

    /// <summary>Gets the referenced (target) table name.</summary>
    public string TargetTable { get; init; } = "";

    /// <summary>Gets the referenced (target) column names.</summary>
    public IReadOnlyList<string> TargetColumns { get; init; } = [];
}
