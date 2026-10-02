using Orkeon.Analysis.Abstractions.DependencyInjection;

namespace Orkeon.Analysis.DependencyInjection;

/// <summary>
/// Host-level settings of the RaggableTree subsystem. A host binds only <see cref="Enabled"/> and
/// <see cref="Embedding"/> from configuration (the <c>RaggableTree</c> section of <c>orkeon run</c>);
/// what an index covers — languages, exclusions, root alias, LLM enrichment — is set per
/// <c>index_codebase</c> call, never here.
/// </summary>
public sealed record RaggableTreeOptions
{
    public bool Enabled { get; init; } = true;

    public EmbeddingOptions Embedding { get; init; } = new();

    /// <summary>
    /// The LLM node summarizer. When <see cref="SummarizerOptions.Provider"/> is not
    /// <see cref="SummarizerProviderKind.None"/>, an <c>index_codebase</c> call with
    /// <c>enrich_with_llm</c> summarizes every node through it; otherwise enrichment is a no-op.
    /// </summary>
    public SummarizerOptions Summarizer { get; init; } = new();

    /// <summary>
    /// When <see langword="true"/> (default), <c>ICitationBlockValidator</c> is registered
    /// and citation blocks written via FileWriteTool are validated against the RaggableTree store.
    /// Set to <see langword="false"/> to disable if you encounter performance issues or false positives.
    /// </summary>
    public bool ValidateCitations { get; init; } = true;
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

    /// <summary>
    /// The model the node summaries are asked of; empty — the default — runs them on the model of
    /// the host's provider (GAP-29: it was <c>claude-haiku-4-5</c>, whatever vendor the host runs).
    /// </summary>
    public string Model { get; init; } = string.Empty;

    public int Concurrency { get; init; } = 5;
}
