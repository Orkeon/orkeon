namespace Orkeon.Infrastructure.Knowledge.Sources;

/// <summary>
/// Configuration options for <see cref="DatabaseKnowledgeSource"/>.
/// </summary>
public class DatabaseKnowledgeSourceOptions
{
    /// <summary>
    /// Gets or sets the ADO.NET connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Gets or sets the ADO.NET provider name (for display/logging purposes).
    /// </summary>
    public string ProviderName { get; set; } = "Microsoft.Data.Sqlite";

    /// <summary>
    /// Gets or sets the SELECT query used to fetch content rows.
    /// </summary>
    public string Query { get; set; } = "";

    /// <summary>
    /// Gets or sets the name of the column containing text content.
    /// </summary>
    public string ContentColumn { get; set; } = "Content";

    /// <summary>
    /// Gets or sets the optional column name used as the title for each row.
    /// </summary>
    public string? TitleColumn { get; set; }

    /// <summary>
    /// Gets or sets the optional column name used as the unique identifier for each row.
    /// </summary>
    public string? IdColumn { get; set; }

    /// <summary>
    /// Gets or sets an optional parameterized query for search.
    /// Use <c>@query</c> as the parameter placeholder.
    /// When null, search falls back to in-memory keyword filtering.
    /// </summary>
    public string? SearchQuery { get; set; }
}
