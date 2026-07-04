using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Graph;

/// <summary>
/// Supported graph database types for schema introspection.
/// </summary>
public enum GraphType
{
    /// <summary>ArcadeDB graph database (via Bolt/Neo4j driver).</summary>
    ArcadeDB,

    /// <summary>JanusGraph graph database (via Gremlin/WebSocket).</summary>
    JanusGraph
}

/// <summary>
/// Request parameters for inspecting graph database schema.
/// </summary>
public record GraphSchemaRequest
{
    /// <summary>Gets the endpoint URI for the graph database.</summary>
    [FieldSchema(Description = "Endpoint URI for the graph database (Bolt URI for ArcadeDB, WebSocket URI for JanusGraph)",
        Example = "ws://localhost:8182/gremlin", IsRequired = true)]
    public string Endpoint { get; init; } = "";

    /// <summary>Gets the graph database type.</summary>
    [FieldSchema(Description = "Graph database type: ArcadeDB or JanusGraph", Example = "JanusGraph", IsRequired = true)]
    public GraphType GraphType { get; init; }

    /// <summary>Gets the database name (required for ArcadeDB).</summary>
    [FieldSchema(Description = "Database name (required for ArcadeDB)", IsRequired = false)]
    public string? Database { get; init; }

    /// <summary>Gets the optional username for authentication.</summary>
    [FieldSchema(Description = "Username for authentication", IsRequired = false)]
    public string? Username { get; init; }

    /// <summary>Gets the optional password for authentication.</summary>
    [FieldSchema(Description = "Password for authentication", IsRequired = false)]
    public string? Password { get; init; }
}
