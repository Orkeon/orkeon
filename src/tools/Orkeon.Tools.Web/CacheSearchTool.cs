using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Memory;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Web;

// ── Request / Response records ────────────────────────────────────────

/// <summary>Strongly-typed request for <see cref="CacheSearchTool"/>.</summary>
public record CacheSearchRequest
{
    /// <summary>Gets the natural-language query to embed and search against the cache.</summary>
    [FieldSchema(Description = "Natural-language query to search semantically against the cached content", Example = "MMR vaccine autism hazard ratio")]
    public string Query { get; init; } = "";

    /// <summary>
    /// Optional source filter. When set, only chunks tagged with this source are returned
    /// (e.g. "web_scrape" to restrict search to web pages cached by web_scrape with cached=true).
    /// When empty, searches across all cached sources.
    /// </summary>
    [FieldSchema(Description = "Optional source tag filter (e.g. \"web_scrape\"). Empty = search all sources.", Example = "web_scrape")]
    public string? Source { get; init; }

    /// <summary>
    /// Optional URL substring filter. When set, only chunks whose source URL contains this
    /// string are returned (e.g. "thelancet.com"). When empty, searches across all URLs.
    /// </summary>
    [FieldSchema(Description = "Optional URL substring filter — only matches chunks whose source URL contains this string. Empty = search all URLs.", Example = "thelancet.com")]
    public Uri? UrlFilter { get; init; }

    /// <summary>Gets the maximum number of chunks to return.</summary>
    [FieldSchema(Description = "Maximum number of chunks to return (default 5)", Example = 5, IsRequired = false)]
    public int TopK { get; init; } = 5;

    /// <summary>Gets the minimum similarity score (0..1) for a chunk to be returned.</summary>
    [FieldSchema(Description = "Minimum cosine-similarity score (0..1) for a chunk to be returned (default 0.0)", Example = 0.3, IsRequired = false)]
    public double MinScore { get; init; } = 0.0;
}

/// <summary>One chunk returned by <see cref="CacheSearchTool"/>.</summary>
public record CacheSearchHit
{
    /// <summary>Gets the source URL or origin identifier of the chunk.</summary>
    [ReturnSchema(Description = "Source URL (or generic source identifier) of this chunk", Example = "https://example.com/article")]
    public Uri? Url { get; init; }

    /// <summary>Gets the page title or document title.</summary>
    [ReturnSchema(Description = "Title (page title for web sources, document title otherwise)", Example = "Example Article")]
    public string Title { get; init; } = "";

    /// <summary>Gets the source tag (e.g. "web_scrape").</summary>
    [ReturnSchema(Description = "Source tag identifying the producing tool", Example = "web_scrape")]
    public string Source { get; init; } = "";

    /// <summary>Gets the chunk text.</summary>
    [ReturnSchema(Description = "The matched chunk content", Example = "The MMR vaccine has been studied in over 13 million children…")]
    public string Content { get; init; } = "";

    /// <summary>Gets the cosine similarity score.</summary>
    [ReturnSchema(Description = "Cosine similarity score (0..1)", Example = 0.82)]
    public float Score { get; init; }
}

/// <summary>Strongly-typed response for <see cref="CacheSearchTool"/>.</summary>
public record CacheSearchResponse
{
    /// <summary>Gets the matched chunks ordered by descending score.</summary>
    [ReturnSchema(Description = "Top-K chunks matching the query, ordered by descending similarity")]
    public IReadOnlyList<CacheSearchHit> Hits { get; init; } = [];

    /// <summary>Gets the number of hits returned.</summary>
    [ReturnSchema(Description = "Number of hits returned", Example = 5)]
    public int HitCount { get; init; }

    /// <summary>Gets the original query.</summary>
    [ReturnSchema(Description = "The query that was executed", Example = "MMR vaccine autism")]
    public string Query { get; init; } = "";
}

// ── Tool ───────────────────────────────────────────────────────────────

/// <summary>
/// Generic semantic search over the RAG cache populated by other tools (e.g. <c>web_scrape</c>
/// with <c>cached=true</c>). Embeds the query, runs <see cref="IMemoryProvider.SearchSimilarAsync"/>,
/// and optionally filters results by source tag and/or URL substring before returning the
/// top-K chunks. Cheap on tokens because only the relevant excerpts hit the agent's context.
/// </summary>
[ToolContract("cache_search",
    Name = "cache_search",
    Description = "Semantic search over content previously stored in the RAG cache (e.g. by web_scrape with cached=true). source filter scopes by producing tool; url_filter scopes by URL substring; both are optional — empty means global search.",
    Category = "Search")]
public partial class CacheSearchTool : ToolBase<CacheSearchRequest, CacheSearchResponse>
{
    private const int MaxTopK = 25;

    private readonly IEmbeddingService _embeddingService;
    private readonly IMemoryProvider _memoryProvider;

    /// <summary>Initializes a new instance of <see cref="CacheSearchTool"/>.</summary>
    public CacheSearchTool(
        IEmbeddingService embeddingService,
        IMemoryProvider memoryProvider,
        ILogger<CacheSearchTool>? logger = null)
        : base(logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _memoryProvider = memoryProvider ?? throw new ArgumentNullException(nameof(memoryProvider));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(CacheSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            return "Query cannot be empty";

        if (request.TopK <= 0 || request.TopK > MaxTopK)
            return $"TopK must be between 1 and {MaxTopK}";

        if (request.MinScore < 0.0 || request.MinScore > 1.0)
            return "MinScore must be between 0.0 and 1.0";

        return null;
    }

    /// <inheritdoc />
    protected override Task<CacheSearchResponse> ExecuteTypedAsync(
        CacheSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<CacheSearchResponse> ExecuteTypedCoreAsync()
        {
            var queryEmbedding = await _embeddingService.GetEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);

            var hasFilters = !string.IsNullOrWhiteSpace(request.Source)
                || request.UrlFilter is not null;

            // Over-fetch when filtering so we still return TopK after exclusion.
            var rawTopK = hasFilters
                ? Math.Min(request.TopK * 4, MaxTopK * 4)
                : request.TopK;

            var raw = await _memoryProvider.SearchSimilarAsync(
                queryEmbedding,
                topK: rawTopK,
                minScore: (float)request.MinScore,
                filter: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var hits = new List<CacheSearchHit>();
            foreach (var scored in raw)
            {
                var hit = TryBuildHit(scored, request);
                if (hit is null)
                    continue;

                hits.Add(hit);

                if (hits.Count >= request.TopK)
                    break;
            }

            var urlFilterLabel = request.UrlFilter?.ToString() ?? "*";
            LogCacheSearchCompleted(request.Query, hits.Count, request.Source ?? "*", urlFilterLabel);

            return new CacheSearchResponse
            {
                Hits = hits,
                HitCount = hits.Count,
                Query = request.Query
            };
        }
    }

    /// <summary>
    /// Applies the source and URL filters to a scored item and, when it passes, maps it to a
    /// <see cref="CacheSearchHit"/>. Returns <c>null</c> when the item is filtered out.
    /// </summary>
    private static CacheSearchHit? TryBuildHit(ScoredMemoryItem scored, CacheSearchRequest request)
    {
        var item = scored.Item;

        if (!MatchesSourceFilter(item.Tags, request.Source))
            return null;

        var props = item.Metadata.CustomProperties;
        var url = props is not null && props.TryGetValue("url", out var u) ? u : item.Source;

        if (!MatchesUrlFilter(url, request.UrlFilter))
            return null;

        var title = props is not null && props.TryGetValue("title", out var t) ? t : "";
        var sourceTag = item.Tags.Count > 0 ? item.Tags[0] : "";

        return new CacheSearchHit
        {
            Url = Uri.TryCreate(url, UriKind.Absolute, out var hitUri) ? hitUri : null,
            Title = title,
            Source = sourceTag,
            Content = item.Content,
            Score = scored.Score
        };
    }

    /// <summary>
    /// Source tag filter — match by tag (e.g. "web_scrape"). When the filter is empty,
    /// items from any source are included.
    /// </summary>
    private static bool MatchesSourceFilter(IReadOnlyList<string> tags, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;

        return tags.Count > 0
            && tags.Any(t => string.Equals(t, source, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// URL substring filter — when empty, all URLs are included.
    /// </summary>
    private static bool MatchesUrlFilter(string url, Uri? urlFilter)
    {
        return urlFilter is null
            || url.Contains(urlFilter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "cache_search query=\"{Query}\" hits={HitCount} source={Source} url_filter={UrlFilter}")]
    private partial void LogCacheSearchCompleted(string query, int hitCount, string source, string urlFilter);
}
