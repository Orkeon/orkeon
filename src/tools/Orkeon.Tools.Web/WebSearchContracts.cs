using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Web;

// ── Shared Web Search Request / Response records ──────────────────────────
// These types are shared across all web search providers (Tavily, Brave, etc.)

/// <summary>
/// Strongly-typed request for web search tools.
/// Shared across all web search providers.
/// </summary>
public sealed record WebSearchRequest
{
    /// <summary>Gets the search query text.</summary>
    [JsonPropertyName("query")]
    [FieldSchema(Description = "The search query text", IsRequired = true, Example = "latest AI frameworks")]
    public string Query { get; init; } = "";

    /// <summary>Gets the maximum number of results to return.</summary>
    [JsonPropertyName("max_results")]
    [FieldSchema(Description = "Maximum number of results to return (default: 5)", IsRequired = false, Example = 5)]
    public int MaxResults { get; init; } = 5;

    /// <summary>Gets the search depth (basic or advanced).</summary>
    [JsonPropertyName("search_depth")]
    [FieldSchema(Description = "Search depth: basic or advanced (default: basic)", IsRequired = false, Example = "basic")]
    public string SearchDepth { get; init; } = "basic";
}

/// <summary>
/// A single web search result item.
/// Shared across all web search providers.
/// </summary>
public sealed record WebSearchResult
{
    /// <summary>Gets the title of the search result.</summary>
    [JsonPropertyName("title")]
    [ReturnSchema(Description = "Title of the search result", Example = "Introduction to AI Frameworks")]
    public string Title { get; init; } = "";

    /// <summary>Gets the URL of the search result.</summary>
    [JsonPropertyName("url")]
    [ReturnSchema(Description = "URL of the search result", Example = "https://example.com/ai-frameworks")]
    public Uri? Url { get; init; }

    /// <summary>Gets the text content/snippet of the search result.</summary>
    [JsonPropertyName("content")]
    [ReturnSchema(Description = "Text content or snippet of the search result", Example = "AI frameworks provide tools for building...")]
    public string Content { get; init; } = "";

    /// <summary>Gets the relevance score (0 to 1, higher is more relevant). Provider-dependent.</summary>
    [JsonPropertyName("score")]
    [ReturnSchema(Description = "Relevance score (0-1, higher is more relevant)", Example = 0.95)]
    public double Score { get; init; }
}

/// <summary>
/// Strongly-typed response for web search tools.
/// Shared across all web search providers.
/// </summary>
public sealed record WebSearchResponse
{
    /// <summary>Gets the search query that was executed.</summary>
    [JsonPropertyName("query")]
    [ReturnSchema(Description = "The search query that was executed", Example = "latest AI frameworks")]
    public string Query { get; init; } = "";

    /// <summary>Gets the list of search results.</summary>
    [JsonPropertyName("results")]
    [ReturnSchema(Description = "List of search results")]
    public IReadOnlyList<WebSearchResult> Results { get; init; } = [];

    /// <summary>Gets the number of results returned.</summary>
    [JsonPropertyName("result_count")]
    [ReturnSchema(Description = "Number of results returned", Example = 5)]
    public int ResultCount { get; init; }
}
