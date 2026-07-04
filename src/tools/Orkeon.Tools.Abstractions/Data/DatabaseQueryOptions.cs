namespace Orkeon.Tools.Abstractions.Data;

/// <summary>
/// Options controlling which SQL operations are permitted during query execution.
/// </summary>
public record DatabaseQueryOptions
{
    /// <summary>Gets whether DDL statements (CREATE, ALTER, DROP) are allowed.</summary>
    public bool AllowDdl { get; init; }

    /// <summary>Gets whether destructive deletes (DELETE without WHERE, TRUNCATE) are allowed.</summary>
    public bool AllowDestructiveDeletes { get; init; }

    /// <summary>
    /// Gets whether multiple statements (separated by ';') are allowed in a single query.
    /// Disabled by default to prevent stacked-query SQL injection (e.g. <c>SELECT 1; DROP TABLE users</c>).
    /// Enable only for trusted batch scripts.
    /// </summary>
    public bool AllowMultiStatement { get; init; }

    /// <summary>Gets the maximum number of rows to return from SELECT queries.</summary>
    public int MaxRows { get; init; } = 1000;

    /// <summary>Gets the query timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
