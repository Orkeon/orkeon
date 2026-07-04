using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using EmbeddingVector = Orkeon.Domain.Memory.EmbeddingVector;
using IEmbeddingService = Orkeon.Application.Interfaces.Ports.IEmbeddingService;

namespace Orkeon.Infrastructure.Tools.Search;

// ── Request / Response records ────────────────────────────────────────
/// <summary>Request parameters for semantic search.</summary>
public record SearchRequest
{
    /// <summary>Gets the search query text.</summary>
    [FieldSchema(Description = "The search query text", Example = "machine learning algorithms")]
    public string Query { get; init; } = "";

    /// <summary>Gets the number of top results to return.</summary>
    [FieldSchema(Description = "Number of top results to return (default: 5)", IsRequired = false, Example = 5)]
    public int TopK { get; init; } = 5;

    /// <summary>Gets the minimum similarity threshold (0 to 1).</summary>
    [FieldSchema(Description = "Minimum similarity threshold 0-1 (default: 0.5)", IsRequired = false, Example = 0.7)]
    public double Threshold { get; init; } = 0.5;
}

/// <summary>A single result item returned by a semantic search.</summary>
public record SearchResultItem
{
    /// <summary>Gets the unique identifier of the memory entry.</summary>
    [ReturnSchema(Description = "Unique identifier of the memory entry", Example = "mem_abc123")]
    public string Id { get; init; } = "";

    /// <summary>Gets the text content of the search result.</summary>
    [ReturnSchema(Description = "Text content of the search result", Example = "Neural networks are a class of machine learning models...")]
    public string Content { get; init; } = "";

    /// <summary>Gets the similarity score (0 to 1, higher is more relevant).</summary>
    [ReturnSchema(Description = "Similarity score (0-1, higher is more relevant)", Example = 0.92f)]
    public float Importance { get; init; }

    /// <summary>Gets the source of the memory entry.</summary>
    [ReturnSchema(Description = "Source of the memory entry", Example = "long_term_memory")]
    public string Source { get; init; } = "";

    /// <summary>Gets the tags associated with the memory entry.</summary>
    [ReturnSchema(Description = "Tags associated with the memory entry")]
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Response from a semantic search operation.</summary>
public record SearchResponse
{
    /// <summary>Gets the search query that was executed.</summary>
    [ReturnSchema(Description = "The search query that was executed", Example = "machine learning algorithms")]
    public string Query { get; init; } = "";

    /// <summary>Gets the matching results ranked by similarity.</summary>
    [ReturnSchema(Description = "Matching results ranked by similarity")]
    public IReadOnlyList<SearchResultItem> Results { get; init; } = [];

    /// <summary>Gets the number of results returned.</summary>
    [ReturnSchema(Description = "Number of results returned", Example = 3)]
    public int ResultCount { get; init; }

    /// <summary>Gets the maximum number of results requested.</summary>
    [ReturnSchema(Description = "Maximum number of results requested", Example = 5)]
    public int TopK { get; init; }

    /// <summary>Gets the similarity threshold used for filtering.</summary>
    [ReturnSchema(Description = "Similarity threshold used for filtering", Example = 0.7)]
    public double Threshold { get; init; }
}

/// <summary>
/// Tool for semantic search using embeddings.
/// Uses IEmbeddingService and IVectorMemoryStore for similarity search.
/// </summary>
[ToolContract("semantic_search",
    Name = "semantic_search",
    Description = "Perform semantic search using embeddings to find relevant information.",
    Category = "Search")]
public partial class SearchTool : ToolBase<SearchRequest, SearchResponse>
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorMemoryStore _vectorStore;

    /// <summary>Initializes a new instance of <see cref="SearchTool"/>.</summary>
    /// <param name="embeddingService">The embedding service used to vectorize queries.</param>
    /// <param name="vectorStore">The vector memory store used for similarity search.</param>
    /// <param name="logger">Optional logger.</param>
    public SearchTool(
        IEmbeddingService embeddingService,
        IVectorMemoryStore vectorStore,
        ILogger<SearchTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);
        _embeddingService = embeddingService;
        ArgumentNullException.ThrowIfNull(vectorStore);
        _vectorStore = vectorStore;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        return null;
    }

    /// <inheritdoc />
    protected override Task<SearchResponse> ExecuteTypedAsync(
        SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<SearchResponse> ExecuteTypedCoreAsync()
        {
            var topK = request.TopK > 0 ? request.TopK : 5;
            var threshold = (float)Math.Clamp(request.Threshold, 0.0, 1.0);

            // Generate embedding for the query
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query).ConfigureAwait(false);
            var embeddingVector = new EmbeddingVector(queryEmbedding);

            // Search the vector store
            var results = await _vectorStore.SearchSimilarAsync(
                embeddingVector,
                topK,
                threshold,
                cancellationToken
            ).ConfigureAwait(false);

            var resultList = results.Select(item => new SearchResultItem
            {
                Id = item.Id,
                Content = item.Content,
                Importance = item.Importance,
                Source = item.Source,
                Tags = item.Tags?.ToList() ?? []
            }).ToList();

            LogSearchCompleted(request.Query, resultList.Count);

            return new SearchResponse
            {
                Query = request.Query,
                Results = resultList,
                ResultCount = resultList.Count,
                TopK = topK,
                Threshold = request.Threshold
            };
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Semantic search for '{Query}' returned {Count} results")]
    private partial void LogSearchCompleted(string query, int count);
}
