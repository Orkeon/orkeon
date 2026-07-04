using Orkeon.Domain.Attributes;
using Orkeon.Tools.Data.Relational.Models;

namespace Orkeon.Tools.Data.Relational;

/// <summary>
/// Response from database schema introspection.
/// </summary>
public record DatabaseSchemaResponse
{
    /// <summary>Gets the tables found in the database.</summary>
    [ReturnSchema(Description = "Tables with their columns")]
    public IReadOnlyList<TableInfo> Tables { get; init; } = [];

    /// <summary>Gets the indexes found, when scope includes Indexes or All.</summary>
    [ReturnSchema(Description = "Index metadata (populated when scope is Indexes or All)")]
    public IReadOnlyList<IndexInfo>? Indexes { get; init; }

    /// <summary>Gets the foreign keys found, when scope includes ForeignKeys or All.</summary>
    [ReturnSchema(Description = "Foreign key metadata (populated when scope is ForeignKeys or All)")]
    public IReadOnlyList<ForeignKeyInfo>? ForeignKeys { get; init; }

    /// <summary>Gets the database name.</summary>
    [ReturnSchema(Description = "Database name from the connection", Example = "mydb")]
    public string DatabaseName { get; init; } = "";

    /// <summary>Gets the provider name that was used.</summary>
    [ReturnSchema(Description = "The ADO.NET provider used for introspection", Example = "Npgsql")]
    public string ProviderName { get; init; } = "";
}
