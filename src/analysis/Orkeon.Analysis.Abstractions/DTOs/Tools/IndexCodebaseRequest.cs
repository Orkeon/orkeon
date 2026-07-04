using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record IndexCodebaseRequest
{
    public string RootPath { get; init; } = "";
    public ImmutableArray<string> Languages { get; init; } = [];
    public ImmutableArray<string> Exclude { get; init; } =
        ["node_modules", "dist", ".git", "bin", "obj"];
    public bool EnrichWithLlm { get; init; }
    public bool IncludeStatements { get; init; }

    /// <summary>
    /// Embedding model identifier. Default is the OpenAI <c>text-embedding-3-small</c>
    /// model (1536 dims) for backward compatibility. When using a different provider
    /// (Ollama <c>nomic-embed-text</c>, local <c>bge-micro-v2</c>, etc.) callers must
    /// provide the matching model identifier explicitly. The actual embedding dimension
    /// is exposed at runtime by <c>IEmbeddingProvider.Dimensions</c> — this field is a
    /// hint only and is not used to size buffers.
    /// </summary>
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public string? SummarizerModel { get; init; } = "claude-haiku-4-5";
    public int SummarizerMaxTokens { get; init; } = 120;
    public int SummarizerConcurrency { get; init; } = 5;

    /// <summary>
    /// Optional alias substituted for the virtual root prefix in every FQN.
    /// When set to "inngest-js" and indexing <c>/src</c>, FQNs switch from
    /// <c>/src::pkg::Module</c> to <c>inngest-js::pkg::Module</c>.
    /// When empty, FQNs use the virtual root (e.g. <c>/src</c>) as the prefix.
    /// </summary>
    public string RootAlias { get; init; } = "";

    /// <summary>
    /// When <see langword="true"/> (default), a <c>.gitignore</c> at the root
    /// is parsed and applied. Disable when indexing trees recovered from
    /// sourcemaps or distribution archives where gitignore would exclude
    /// everything the caller actually wants indexed.
    /// </summary>
    public bool RespectGitignore { get; init; } = true;
}
