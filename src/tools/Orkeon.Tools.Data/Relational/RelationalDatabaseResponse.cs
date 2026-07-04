using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Response from executing a SQL query on a relational database.
/// </summary>
public record RelationalDatabaseResponse
{
    /// <summary>Gets the column names from the result set.</summary>
    [ReturnSchema(Description = "Column names from the result set")]
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>Gets the result rows as key-value pairs mapping column name to cell value.</summary>
    [ReturnSchema(Description = "Result rows as key-value pairs (column name → value)")]
    public IReadOnlyList<Dictionary<string, object>>? Rows { get; init; }

    /// <summary>Gets the number of rows returned by a SELECT query.</summary>
    [ReturnSchema(Description = "Number of rows returned by SELECT", Example = 42)]
    public int? RowCount { get; init; }

    /// <summary>Gets the number of columns in the result set.</summary>
    [ReturnSchema(Description = "Number of columns in the result set", Example = 3)]
    public int? ColumnCount { get; init; }

    /// <summary>Gets the number of rows affected by an INSERT/UPDATE/DELETE statement.</summary>
    [ReturnSchema(Description = "Number of rows affected by INSERT/UPDATE/DELETE", Example = 7)]
    public int? AffectedRows { get; init; }

    /// <summary>Gets the scalar result value from a Scalar query.</summary>
    [ReturnSchema(Description = "Scalar result value from a Scalar query")]
    public object? ScalarResult { get; init; }

    /// <summary>Gets the type of query that was executed.</summary>
    [ReturnSchema(Description = "The type of query that was executed", Example = "Select")]
    public RelationalQueryType QueryType { get; init; } = RelationalQueryType.Select;

    /// <summary>Gets the execution time in milliseconds.</summary>
    [ReturnSchema(Description = "Query execution time in milliseconds", Example = 42)]
    public long ExecutionTimeMs { get; init; }

    /// <summary>Gets the provider name that was used.</summary>
    [ReturnSchema(Description = "The ADO.NET provider that executed the query", Example = "Npgsql")]
    public string ProviderName { get; init; } = "";
}
