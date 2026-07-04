using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;
using static Orkeon.Domain.Constants.Memory.SearchDefaults;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Configuration options for the LanceDB memory provider, backed by a remote
/// LanceDB Cloud / Enterprise server speaking the Lance REST Namespace protocol
/// (<c>https://docs.lancedb.com/api-reference/rest/</c>).
/// </summary>
public class LanceDbOptions
{
    /// <summary>
    /// Gets or sets the base URL of the LanceDB Cloud/Enterprise REST endpoint
    /// (e.g. <c>https://my-deployment.us-east-1.api.lancedb.com</c>).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the API key sent in the <c>x-api-key</c> header.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the optional database name sent in the <c>x-lancedb-database</c>
    /// header (Enterprise deployments behind a shared/private endpoint).
    /// </summary>
    public string? Database { get; set; }

    /// <summary>Gets or sets the table name used for storing memories.</summary>
    public string TableName { get; set; } = "orkeon_memories";

    /// <summary>Gets or sets the default number of results to return from queries.</summary>
    public int DefaultTopK { get; set; } = 10;

    /// <summary>
    /// Gets or sets the embedding vector dimension of the table's fixed-size
    /// <c>vector</c> column. Must match the embeddings stored through the provider.
    /// </summary>
    public int EmbeddingDimension { get; set; } = DefaultDimension;

    /// <summary>Gets or sets the minimum similarity score threshold for vector search results.</summary>
    public float MinSimilarityScore { get; set; }

    /// <summary>
    /// Gets or sets the distance metric used by server-side vector search
    /// (<c>cosine</c>, <c>l2</c> or <c>dot</c>). The provider converts the returned
    /// <c>_distance</c> to a similarity score as <c>1 - distance</c>, which is only
    /// meaningful for the default <c>cosine</c> metric.
    /// </summary>
    public string DistanceType { get; set; } = "cosine";

    /// <summary>
    /// Gets or sets the weight applied to the server-side vector similarity ranking
    /// when fusing hybrid search results.
    /// </summary>
    public float VectorWeight { get; set; } = (float)DefaultVectorWeight;

    /// <summary>
    /// Gets or sets the weight applied to the server-side full-text (BM25) ranking
    /// when fusing hybrid search results.
    /// </summary>
    public float FullTextWeight { get; set; } = 0.3f;

    /// <summary>
    /// Gets or sets a value indicating whether the provider creates a full-text (FTS)
    /// index on the <c>content</c> column when it creates the table. Server-side
    /// full-text search requires this index; creation failures are logged as warnings
    /// and surface later as server errors on <c>SearchAsync</c>.
    /// </summary>
    public bool CreateFullTextIndexOnInit { get; set; } = true;
}
