using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record IndexCodebaseResponse
{
    [JsonPropertyName("index_id")]
    public required string IndexId { get; init; }

    [JsonPropertyName("node_count")]
    public required int NodeCount { get; init; }

    [JsonPropertyName("edge_count")]
    public required int EdgeCount { get; init; }

    [JsonPropertyName("statement_count")]
    public int StatementCount { get; init; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; init; }

    [JsonPropertyName("elapsed")]
    public TimeSpan Elapsed { get; init; }

    [JsonPropertyName("errors")]
    public ImmutableArray<string> Errors { get; init; } = [];

    [JsonPropertyName("embedding_stats")]
    public EmbeddingStats? EmbeddingStats { get; init; }

    /// <summary>
    /// Diagnostic counters describing why the discovery yielded the file
    /// set it did. When <see cref="FileCount"/> is 0, these counters tell
    /// the caller whether the mount was actually empty, whether the
    /// gitignore filtered everything out, or whether the language filter
    /// dropped every candidate. All counters default to 0.
    /// </summary>
    [JsonPropertyName("filter_stats")]
    public IndexFilterStats FilterStats { get; init; } = new();
}

public sealed record IndexFilterStats
{
    [JsonPropertyName("total_enumerated")]
    public int TotalEnumerated { get; init; }

    [JsonPropertyName("excluded_by_exclude_set")]
    public int ExcludedByExcludeSet { get; init; }

    [JsonPropertyName("excluded_by_gitignore")]
    public int ExcludedByGitignore { get; init; }

    [JsonPropertyName("excluded_by_language")]
    public int ExcludedByLanguage { get; init; }

    [JsonPropertyName("excluded_by_suffix")]
    public int ExcludedBySuffix { get; init; }

    [JsonPropertyName("vfs_file_entries_yielded")]
    public int VfsFileEntriesYielded { get; init; }

    [JsonPropertyName("physical_entries_probe")]
    public int PhysicalEntriesProbe { get; init; }

    [JsonPropertyName("physical_root")]
    public string PhysicalRoot { get; init; } = "";
}

public sealed record IncrementalReindexRequest
{
    public string RootPath { get; init; } = "";
    public ImmutableArray<string> ChangedFiles { get; init; } = [];
    public string? FromCommit { get; init; }
    public string? ToCommit { get; init; }
    public ImmutableArray<string> Languages { get; init; } = [];
    public bool EnrichWithLlm { get; init; }
}
