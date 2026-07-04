using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.FileSystem;

// -- Request / Response records ──────────────────────────────────────────

/// <summary>Request parameters for the directory_search tool.</summary>
public record DirectorySearchRequest
{
    /// <summary>Root directory to search.</summary>
    [FieldSchema(Description = "Root directory to search", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>Semantic search query.</summary>
    [FieldSchema(Description = "Semantic search query", Example = "error handling patterns")]
    public string Query { get; init; } = "";

    /// <summary>Glob patterns separated by ';' to filter files (default: '*').</summary>
    [FieldSchema(Description = "Glob patterns separated by ';' to filter files (default: '*')", IsRequired = false, Example = "*.cs;*.txt")]
    public string FilePatterns { get; init; } = "*";

    /// <summary>Whether to search subdirectories recursively (default: true).</summary>
    [FieldSchema(Description = "Whether to search subdirectories recursively (default: true)", IsRequired = false, Example = true)]
    public bool Recursive { get; init; } = true;

    /// <summary>Number of top results to return (default: 5).</summary>
    [FieldSchema(Description = "Number of top results to return (default: 5)", IsRequired = false, Example = 5)]
    public int TopK { get; init; } = 5;

    /// <summary>Minimum similarity threshold 0-1 (default: 0.3).</summary>
    [FieldSchema(Description = "Minimum similarity threshold 0-1 (default: 0.3)", IsRequired = false, Example = 0.3)]
    public double Threshold { get; init; } = 0.3;

    /// <summary>Maximum chunk size in characters (default: 500).</summary>
    [FieldSchema(Description = "Maximum chunk size in characters (default: 500)", IsRequired = false, Example = 500)]
    public int ChunkSize { get; init; } = 500;

    /// <summary>Skip files larger than this size in KB (default: 512).</summary>
    [FieldSchema(Description = "Skip files larger than this size in KB (default: 512)", IsRequired = false, Example = 512)]
    public int MaxFileSizeKb { get; init; } = 512;
}

/// <summary>A single semantic search result from directory search.</summary>
public record DirectorySearchResult
{
    /// <summary>The matched text content.</summary>
    [ReturnSchema(Description = "The matched text content")]
    public string Content { get; init; } = "";

    /// <summary>Similarity score (0-1, higher is more relevant).</summary>
    [ReturnSchema(Description = "Similarity score (0-1, higher is more relevant)", Example = 0.85f)]
    public float Score { get; init; }

    /// <summary>Relative path to the source file.</summary>
    [ReturnSchema(Description = "Relative path to the source file", Example = "src/MyClass.cs")]
    public string SourceFile { get; init; } = "";

    /// <summary>Approximate line number where the chunk starts.</summary>
    [ReturnSchema(Description = "Approximate line number where the chunk starts", Example = 42)]
    public int ApproximateLine { get; init; }

    /// <summary>Zero-based index of the chunk within the file.</summary>
    [ReturnSchema(Description = "Zero-based index of the chunk within the file", Example = 0)]
    public int ChunkIndex { get; init; }

    /// <summary>File extension of the source file.</summary>
    [ReturnSchema(Description = "File extension of the source file", Example = ".cs")]
    public string FileExtension { get; init; } = "";
}

/// <summary>Response returned by the directory_search tool.</summary>
public record DirectorySearchResponse
{
    /// <summary>Matching results ranked by similarity.</summary>
    [ReturnSchema(Description = "Matching results ranked by similarity")]
    public IReadOnlyList<DirectorySearchResult> Results { get; init; } = [];

    /// <summary>Number of results returned.</summary>
    [ReturnSchema(Description = "Number of results returned", Example = 3)]
    public int ResultCount { get; init; }

    /// <summary>Total number of chunks that were indexed.</summary>
    [ReturnSchema(Description = "Total number of chunks that were indexed", Example = 120)]
    public int TotalChunks { get; init; }

    /// <summary>Number of files that were successfully processed.</summary>
    [ReturnSchema(Description = "Number of files that were successfully processed", Example = 15)]
    public int FilesProcessed { get; init; }

    /// <summary>Number of files that were skipped (binary, too large, etc.).</summary>
    [ReturnSchema(Description = "Number of files that were skipped (binary, too large, etc.)", Example = 2)]
    public int FilesSkipped { get; init; }

    /// <summary>The search query that was executed.</summary>
    [ReturnSchema(Description = "The search query that was executed", Example = "error handling patterns")]
    public string Query { get; init; } = "";
}
