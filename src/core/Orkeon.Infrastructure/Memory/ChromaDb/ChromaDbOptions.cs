using Orkeon.Infrastructure.Constants.Llm;

namespace Orkeon.Infrastructure.Memory.ChromaDb;

/// <summary>
/// Configuration options for the ChromaDB memory provider.
/// Targets the ChromaDB REST API v2 (servers ≥ 0.6.x, including 1.x);
/// the legacy <c>/api/v1</c> surface was removed by recent ChromaDB releases.
/// </summary>
public class ChromaDbOptions
{
    /// <summary>Default tenant name used by ChromaDB single-tenant deployments.</summary>
    public const string DefaultTenant = "default_tenant";

    /// <summary>Default database name used by ChromaDB single-tenant deployments.</summary>
    public const string DefaultDatabase = "default_database";

    /// <summary>Gets or sets the base URL of the ChromaDB server.</summary>
    public Uri BaseUrl { get; set; } = new(LlmEndpoints.ChromaDbDefault);

    /// <summary>
    /// Gets or sets the tenant addressed by the v2 API
    /// (<c>/api/v2/tenants/{tenant}/...</c>). Defaults to <c>default_tenant</c>.
    /// Non-default tenants must already exist on the server.
    /// </summary>
    public string Tenant { get; set; } = DefaultTenant;

    /// <summary>
    /// Gets or sets the database addressed by the v2 API
    /// (<c>/api/v2/tenants/{tenant}/databases/{database}/...</c>).
    /// Defaults to <c>default_database</c>. Non-default databases must already exist on the server.
    /// </summary>
    public string Database { get; set; } = DefaultDatabase;

    /// <summary>Gets or sets the collection name to use for storing memories.</summary>
    public string CollectionName { get; set; } = "orkeon_memories";

    /// <summary>Gets or sets the default number of results to return from queries.</summary>
    public int DefaultTopK { get; set; } = 10;
}
