using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Query languages supported by ArcadeDB via the Bolt protocol.
/// </summary>
public enum ArcadeDbQueryLanguage
{
    /// <summary>Cypher query language (default).</summary>
    Cypher,

    /// <summary>SQL query language.</summary>
    Sql
}

/// <summary>
/// Request parameters for executing a query on ArcadeDB via the Bolt protocol.
/// </summary>
public record ArcadeDbRequest
{
    /// <summary>Gets the Bolt protocol URI for ArcadeDB.</summary>
    [FieldSchema(Description = "Bolt protocol URI for ArcadeDB", Example = "bolt://localhost:2480", IsRequired = true)]
    public Uri? BoltUri { get; init; }

    /// <summary>Gets the target database name.</summary>
    [FieldSchema(Description = "Target database name in ArcadeDB", Example = "mydb", IsRequired = true)]
    public string Database { get; init; } = "";

    /// <summary>Gets the optional username for authentication.</summary>
    [FieldSchema(Description = "Username for ArcadeDB authentication", IsRequired = false)]
    public string? Username { get; init; }

    /// <summary>Gets the optional password for authentication.</summary>
    [FieldSchema(Description = "Password for ArcadeDB authentication", IsRequired = false)]
    public string? Password { get; init; }

    /// <summary>Gets the Cypher or SQL query to execute.</summary>
    [FieldSchema(Description = "Cypher or SQL query to execute", Example = "MATCH (n) RETURN n LIMIT 10", IsRequired = true)]
    public string Query { get; init; } = "";

    /// <summary>Gets the optional query parameters.</summary>
    [FieldSchema(Description = "Query parameters as key-value pairs", IsRequired = false)]
    public Dictionary<string, object>? Parameters { get; init; }

    /// <summary>Gets the query language to use.</summary>
    [FieldSchema(Description = "Query language: Cypher (default) or Sql", IsRequired = false, Example = "Cypher")]
    public ArcadeDbQueryLanguage QueryLanguage { get; init; } = ArcadeDbQueryLanguage.Cypher;

    /// <summary>Gets the maximum number of result records to return.</summary>
    [FieldSchema(Description = "Maximum number of result records to return", IsRequired = false, Example = "1000")]
    public int MaxResults { get; init; } = 1000;
}
