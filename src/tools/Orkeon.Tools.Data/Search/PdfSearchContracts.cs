using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Data.Search;

// ── Request ──────────────────────────────────────────────────────────

/// <summary>
/// Request parameters for semantic search within PDF documents.
/// </summary>
public record PdfSearchRequest
{
    /// <summary>Gets the file path of a PDF or a directory containing PDFs.</summary>
    [FieldSchema(Description = "Path to a PDF file or a directory of PDFs", Example = "/docs/report.pdf")]
    public string Path { get; init; } = "";

    /// <summary>Gets the semantic search query.</summary>
    [FieldSchema(Description = "The semantic search query", Example = "machine learning algorithms")]
    public string Query { get; init; } = "";

    /// <summary>Gets the optional page range filter (e.g., '1-5', '1,3,5', '2-').</summary>
    [FieldSchema(Description = "Optional page range (e.g., '1-5', '1,3,5', '2-'). Default: all pages.",
        IsRequired = false, Example = "1-5")]
    public string? PageRange { get; init; }

    /// <summary>Gets the maximum number of results to return.</summary>
    [FieldSchema(Description = "Maximum number of results to return", IsRequired = false, Default = 5, Example = 5)]
    public int TopK { get; init; } = 5;

    /// <summary>Gets the minimum similarity threshold (0.0 to 1.0).</summary>
    [FieldSchema(Description = "Minimum cosine similarity threshold (0.0 to 1.0)", IsRequired = false, Default = 0.3, Example = 0.3)]
    public double Threshold { get; init; } = 0.3;

    /// <summary>Gets the chunk size for text splitting (in characters).</summary>
    [FieldSchema(Description = "Chunk size in characters for text splitting", IsRequired = false, Default = 500, Example = 500)]
    public int ChunkSize { get; init; } = 500;
}

// ── Response ─────────────────────────────────────────────────────────

/// <summary>
/// Response from a PDF semantic search operation.
/// </summary>
public record PdfSearchResponse
{
    /// <summary>Gets the matching results ranked by similarity score.</summary>
    [ReturnSchema(Description = "Matching results ranked by similarity score")]
    public IReadOnlyList<PdfSearchResult> Results { get; init; } = [];

    /// <summary>Gets the number of results returned.</summary>
    [ReturnSchema(Description = "Number of results returned", Example = 3)]
    public int ResultCount { get; init; }

    /// <summary>Gets the total number of chunks generated from the PDF(s).</summary>
    [ReturnSchema(Description = "Total number of text chunks generated", Example = 42)]
    public int TotalChunks { get; init; }

    /// <summary>Gets the total number of pages processed.</summary>
    [ReturnSchema(Description = "Total number of pages processed", Example = 10)]
    public int TotalPages { get; init; }

    /// <summary>Gets the number of PDF files processed.</summary>
    [ReturnSchema(Description = "Number of PDF files processed", Example = 1)]
    public int FilesProcessed { get; init; }

    /// <summary>Gets the search query that was used.</summary>
    [ReturnSchema(Description = "The search query that was used", Example = "machine learning algorithms")]
    public string Query { get; init; } = "";
}

// ── Result item ──────────────────────────────────────────────────────

/// <summary>
/// A single result from a PDF semantic search.
/// </summary>
public record PdfSearchResult
{
    /// <summary>Gets the text content of the matching chunk.</summary>
    [ReturnSchema(Description = "Text content of the matching chunk")]
    public string Content { get; init; } = "";

    /// <summary>Gets the cosine similarity score (0.0 to 1.0).</summary>
    [ReturnSchema(Description = "Cosine similarity score (0.0 to 1.0)", Example = 0.87)]
    public float Score { get; init; }

    /// <summary>Gets the source PDF file path.</summary>
    [ReturnSchema(Description = "Source PDF file path", Example = "/docs/report.pdf")]
    public string SourceFile { get; init; } = "";

    /// <summary>Gets the 1-based page number where the chunk was extracted.</summary>
    [ReturnSchema(Description = "1-based page number", Example = 3)]
    public int PageNumber { get; init; }

    /// <summary>Gets the 0-based chunk index within the page.</summary>
    [ReturnSchema(Description = "0-based chunk index within the page", Example = 0)]
    public int ChunkIndex { get; init; }
}
