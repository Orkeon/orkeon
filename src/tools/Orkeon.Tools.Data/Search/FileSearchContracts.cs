using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Search;

// ── Request records ──────────────────────────────────────────────────

/// <summary>
/// Base request parameters for file-based semantic search tools.
/// </summary>
public abstract record FileSearchRequestBase
{
    /// <summary>Gets the file or directory path to search within.</summary>
    [FieldSchema(Description = "File or directory path to search within", Example = "/docs/notes.txt")]
    public string Path { get; init; } = "";

    /// <summary>Gets the semantic search query.</summary>
    [FieldSchema(Description = "Semantic search query", Example = "how to configure logging")]
    public string Query { get; init; } = "";

    /// <summary>Gets the number of top results to return.</summary>
    [FieldSchema(Description = "Number of top results to return (default: 5)", IsRequired = false, Example = 5)]
    public int TopK { get; init; } = 5;

    /// <summary>Gets the minimum similarity threshold (0 to 1).</summary>
    [FieldSchema(Description = "Minimum similarity threshold 0-1 (default: 0.3)", IsRequired = false, Example = 0.3)]
    public double Threshold { get; init; } = 0.3;

    /// <summary>Gets the maximum chunk size in characters.</summary>
    [FieldSchema(Description = "Maximum chunk size in characters (default: 500)", IsRequired = false, Example = 500)]
    public int ChunkSize { get; init; } = 500;
}

/// <summary>
/// Request parameters for plain text (.txt) file search.
/// </summary>
public record TxtSearchRequest : FileSearchRequestBase;

/// <summary>
/// Request parameters for Markdown (.md, .mdx) file search.
/// </summary>
public record MdxSearchRequest : FileSearchRequestBase;

// ── Response records ─────────────────────────────────────────────────

/// <summary>
/// A single search result from a file-based semantic search.
/// </summary>
public record FileSearchResult
{
    /// <summary>Gets the matched text content.</summary>
    [ReturnSchema(Description = "Matched text content")]
    public string Content { get; init; } = "";

    /// <summary>Gets the similarity score (0 to 1).</summary>
    [ReturnSchema(Description = "Similarity score (0-1, higher is more relevant)", Example = 0.85f)]
    public float Score { get; init; }

    /// <summary>Gets the source file path.</summary>
    [ReturnSchema(Description = "Source file path", Example = "/docs/notes.txt")]
    public string SourceFile { get; init; } = "";

    /// <summary>Gets the approximate starting line number in the source file.</summary>
    [ReturnSchema(Description = "Approximate starting line number in the source file", Example = 42)]
    public int ApproximateLine { get; init; }

    /// <summary>Gets the chunk index within the file.</summary>
    [ReturnSchema(Description = "Chunk index within the file", Example = 3)]
    public int ChunkIndex { get; init; }
}

/// <summary>
/// Response from a file-based semantic search operation.
/// </summary>
public record FileSearchResponse
{
    /// <summary>Gets the search results ranked by similarity.</summary>
    [ReturnSchema(Description = "Search results ranked by similarity")]
    public IReadOnlyList<FileSearchResult> Results { get; init; } = [];

    /// <summary>Gets the number of results returned.</summary>
    [ReturnSchema(Description = "Number of results returned", Example = 3)]
    public int ResultCount { get; init; }

    /// <summary>Gets the total number of chunks processed.</summary>
    [ReturnSchema(Description = "Total number of chunks processed", Example = 25)]
    public int TotalChunks { get; init; }

    /// <summary>Gets the number of files processed.</summary>
    [ReturnSchema(Description = "Number of files processed", Example = 4)]
    public int FilesProcessed { get; init; }

    /// <summary>Gets the search query that was executed.</summary>
    [ReturnSchema(Description = "The search query that was executed", Example = "how to configure logging")]
    public string Query { get; init; } = "";
}
