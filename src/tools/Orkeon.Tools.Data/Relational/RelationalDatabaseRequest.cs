using Orkeon.Domain.Attributes;
using Orkeon.Tools.Data.Constants.Database;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Query types supported by the RelationalDatabaseTool.
/// </summary>
public enum RelationalQueryType
{
    /// <summary>A SELECT query that returns result rows.</summary>
    Select,

    /// <summary>A non-query statement (INSERT/UPDATE/DELETE) that returns affected row count.</summary>
    Execute,

    /// <summary>A scalar query that returns a single value.</summary>
    Scalar
}

/// <summary>
/// Request parameters for executing a SQL query on a relational database.
/// </summary>
public record RelationalDatabaseRequest
{
    /// <summary>Gets the database connection string.</summary>
    [FieldSchema(Description = "Database connection string", Example = "Host=localhost;Database=mydb;Username=myuser;")]
    public string ConnectionString { get; init; } = "";

    /// <summary>Gets the ADO.NET provider name.</summary>
    [FieldSchema(Description = "ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite", Example = "Npgsql")]
    public string ProviderName { get; init; } = "";

    /// <summary>Gets the SQL query to execute.</summary>
    [FieldSchema(Description = "SQL query to execute", Example = "SELECT name, email FROM users WHERE active = @active")]
    public string Query { get; init; } = "";

    /// <summary>Gets the type of query: Select, Execute, or Scalar.</summary>
    [FieldSchema(Description = "Type of query: Select (returns rows), Execute (returns affected rows count), or Scalar (returns single value)", IsRequired = false, Example = "Select")]
    public RelationalQueryType QueryType { get; init; } = RelationalQueryType.Select;

    /// <summary>Gets optional named parameters for parameterized queries.</summary>
    [FieldSchema(Description = "Optional named parameters for parameterized queries (e.g. {\"active\": true})", IsRequired = false)]
    public Dictionary<string, object>? Parameters { get; init; }

    /// <summary>Gets the maximum number of rows to return for SELECT queries.</summary>
    [FieldSchema(Description = "Maximum number of rows to return for SELECT queries", IsRequired = false, Example = "1000")]
    public int MaxRows { get; init; } = DatabaseDefaults.DefaultMaxRows;

    /// <summary>Gets the query timeout in seconds.</summary>
    [FieldSchema(Description = "Query timeout in seconds", IsRequired = false, Example = "30")]
    public int TimeoutSeconds { get; init; } = DatabaseDefaults.DefaultTimeoutSeconds;
}
