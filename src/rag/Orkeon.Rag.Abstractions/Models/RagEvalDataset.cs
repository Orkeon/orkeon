using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A versioned golden dataset (plan §9.1): the evaluation cases plus optional
/// corpus/collection defaults. Loaded from YAML (see
/// <c>examples/rag/eval/golden.yaml</c> and the loader in <c>Orkeon.Rag</c>).
/// </summary>
public sealed record RagEvalDataset
{
    /// <summary>Dataset name (defaults to the dataset file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional corpus directory ingested before evaluation. Either an absolute
    /// virtual path (<c>/workspace/…</c>) or a path relative to the dataset file
    /// (e.g. <c>./corpus</c>). <c>null</c> means the collection is assumed to be
    /// already ingested.
    /// </summary>
    public string? CorpusPath { get; init; }

    /// <summary>
    /// Default collection evaluated when the caller names none.
    /// <c>null</c> falls back to <c>rag-eval-{Name}</c>.
    /// </summary>
    public string? DefaultCollection { get; init; }

    /// <summary>The evaluation cases (never empty for a valid dataset).</summary>
    public required ImmutableList<RagEvalCase> Cases { get; init; }
}
