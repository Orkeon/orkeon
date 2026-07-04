using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Specifies which schema elements to retrieve.
/// </summary>
public enum SchemaScope
{
    /// <summary>Retrieve table names and their columns.</summary>
    Tables,

    /// <summary>Retrieve column details only.</summary>
    Columns,

    /// <summary>Retrieve index metadata only.</summary>
    Indexes,

    /// <summary>Retrieve foreign key metadata only.</summary>
    ForeignKeys,

    /// <summary>Retrieve all schema elements.</summary>
    All
}

/// <summary>
/// Request parameters for database schema introspection.
/// </summary>
public record DatabaseSchemaRequest
{
    /// <summary>Gets the database connection string.</summary>
    [FieldSchema(Description = "Database connection string", Example = "Host=localhost;Database=mydb;Username=myuser;")]
    public string ConnectionString { get; init; } = "";

    /// <summary>Gets the ADO.NET provider name.</summary>
    [FieldSchema(Description = "ADO.NET provider name: Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Microsoft.Data.Sqlite", Example = "Npgsql")]
    public string ProviderName { get; init; } = "";

    /// <summary>Gets the scope of schema elements to retrieve.</summary>
    [FieldSchema(Description = "Schema scope: Tables (default), Columns, Indexes, ForeignKeys, or All", IsRequired = false, Example = "Tables")]
    public SchemaScope SchemaScope { get; init; } = SchemaScope.Tables;

    /// <summary>Gets an optional table name filter supporting wildcard (*) matching.</summary>
    [FieldSchema(Description = "Optional table name filter with wildcard support (e.g. 'user*', '*order*')", IsRequired = false, Example = "user*")]
    public string? TableFilter { get; init; }
}
