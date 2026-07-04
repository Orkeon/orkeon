using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DependencyInjection;

namespace Orkeon.Analysis.DependencyInjection;

public sealed record RaggableTreeOptions
{
    public bool Enabled { get; init; } = true;

    public ImmutableArray<string> Languages { get; init; } = [];

    public ImmutableArray<string> Exclude { get; init; } =
        ["node_modules", "dist", ".git", "bin", "obj"];

    /// <summary>
    /// Optional alias used to shorten FQN: when set, the absolute disk root path
    /// is replaced with this alias in every node FQN. Example: "inngest-js" →
    /// FQN "inngest-js::pkg::Module" instead of "/workspace/.../inngest-js::pkg::Module".
    /// </summary>
    public string RootAlias { get; init; } = "";

    public RaggableTreeIndexMode IndexMode { get; init; } = RaggableTreeIndexMode.Frozen;

    public bool EnrichWithLlm { get; init; }

    public bool IncludeStatements { get; init; }

    public EmbeddingOptions Embedding { get; init; } = new();

    public SummarizerOptions Summarizer { get; init; } = new();

    public VectorStoreOptions VectorStore { get; init; } = new();

    public CacheOptions Cache { get; init; } = new();

    /// <summary>
    /// When <see langword="true"/> (default), <c>ICitationBlockValidator</c> is registered
    /// and citation blocks written via FileWriteTool are validated against the RaggableTree store.
    /// Set to <see langword="false"/> to disable if you encounter performance issues or false positives.
    /// </summary>
    public bool ValidateCitations { get; init; } = true;
}

public enum RaggableTreeIndexMode
{
    Frozen,
    Live,
    BreakOnChange,
}

public enum EmbeddingProviderKind
{
    None,
    OpenAI,
    Ollama,
    Onnx,
    LocalSmartComponents,
}

public enum SummarizerProviderKind
{
    None,
    Anthropic,
}

public enum VectorStoreKind
{
    InMemory,
    Redis,
    ChromaDb,
    Pinecone,
    LanceDb,
    Sqlite,
}

public sealed record EmbeddingOptions
{
    public EmbeddingProviderKind Provider { get; init; } = EmbeddingProviderKind.None;

    /// <summary>
    /// Embedding model identifier. When empty, each provider falls back to its own default
    /// (OpenAI → <c>"text-embedding-3-small"</c>, Ollama → <c>"nomic-embed-text"</c>,
    /// LocalSmartComponents → <c>"bge-micro-v2"</c>). Required only when overriding the
    /// provider default.
    /// </summary>
    public string Model { get; init; } = "";

    public string? ApiKey { get; init; }

    public Uri? BaseUrl { get; init; }

    public int? Dimensions { get; init; }

    /// <summary>
    /// Per-text character cap applied before embedding. Texts longer than this are truncated.
    /// Protects against backends with a fixed physical_batch_size (e.g. llama.cpp default
    /// 512 tokens) that would 500 on long inputs. Rule of thumb: pick ~1× the token budget
    /// (worst-case 1 char/token on dense code). Leave <see langword="null"/> to disable.
    /// Typical value: 500 to stay safely under a 512-token server limit.
    /// </summary>
    public int? MaxTextChars { get; init; }

    /// <summary>
    /// Optional configuration block for the on-device local embedding provider
    /// (used when <see cref="Provider"/> is <see cref="EmbeddingProviderKind.LocalSmartComponents"/>).
    /// <see langword="null"/> is valid: defaults are applied at the provider level.
    /// </summary>
    public LocalEmbeddingOptions? Local { get; init; }
}

public sealed record SummarizerOptions
{
    public SummarizerProviderKind Provider { get; init; } = SummarizerProviderKind.None;

    public string Model { get; init; } = "claude-haiku-4-5";

    public int Concurrency { get; init; } = 5;
}

public sealed record VectorStoreOptions
{
    public VectorStoreKind Provider { get; init; } = VectorStoreKind.InMemory;
}

public sealed record CacheOptions
{
    public bool Enabled { get; init; } = true;

    public string Path { get; init; } = ".orkeon/raggable-tree.json";
}
