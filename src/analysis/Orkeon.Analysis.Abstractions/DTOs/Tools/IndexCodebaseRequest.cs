using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

/// <summary>
/// The settings of one <c>index_codebase</c> call — exactly what the build reads. Statements are
/// always extracted. An argument this record does not declare is ignored, not refused.
/// </summary>
public sealed record IndexCodebaseRequest
{
    public string RootPath { get; init; } = "";
    public ImmutableArray<string> Languages { get; init; } = [];
    public ImmutableArray<string> Exclude { get; init; } =
        ["node_modules", "dist", ".git", "bin", "obj"];
    public bool EnrichWithLlm { get; init; }

    /// <summary>
    /// Embedding model identifier. Default is the OpenAI <c>text-embedding-3-small</c>
    /// model (1536 dims) for backward compatibility. When using a different provider
    /// (Ollama <c>nomic-embed-text</c>, local <c>bge-micro-v2</c>, etc.) callers must
    /// provide the matching model identifier explicitly. The actual embedding dimension
    /// is exposed at runtime by <c>IEmbeddingProvider.Dimensions</c> — this field is a
    /// hint only and is not used to size buffers.
    /// </summary>
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    /// <summary>
    /// Optional alias substituted for the virtual root prefix in every FQN.
    /// When set to "inngest-js" and indexing <c>/src</c>, FQNs switch from
    /// <c>/src::pkg::Module</c> to <c>inngest-js::pkg::Module</c>.
    /// When empty, FQNs use the virtual root (e.g. <c>/src</c>) as the prefix.
    /// </summary>
    public string RootAlias { get; init; } = "";

    /// <summary>
    /// When <see langword="true"/> (default), the ignore files of the tree are applied as git applies
    /// them: <c>.git/info/exclude</c>, the root <c>.gitignore</c> and each nested <c>.gitignore</c> for the
    /// paths under its folder. Disable when indexing trees recovered from sourcemaps or distribution
    /// archives where gitignore would exclude everything the caller actually wants indexed.
    /// </summary>
    public bool RespectGitignore { get; init; } = true;

    /// <summary>
    /// How many files are read and parsed at once. <c>0</c> (default) uses one per processor, <c>1</c> parses
    /// them one after the other; a negative value is refused. The index is the same whatever the value: files are
    /// merged in the order they were discovered.
    /// </summary>
    public int MaxParallelism { get; init; }

    /// <summary>
    /// When <see langword="true"/> (default) and the host wires the index cache, the index saved under
    /// <c>&lt;root&gt;/.orkeon/cache/raggable-tree/</c> by an earlier call is reloaded instead of parsing the codebase
    /// again: as is when no file changed, refreshed file by file when some did. <see langword="false"/> rebuilds
    /// from scratch, then saves the new index for the next call.
    /// </summary>
    public bool UseCache { get; init; } = true;
}
