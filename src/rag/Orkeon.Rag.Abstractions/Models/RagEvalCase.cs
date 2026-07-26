using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// One question of a golden evaluation dataset (plan §9.1): the question text,
/// the references of the sources/chunks a correct retrieval must surface, and
/// the deterministic expectations used by the heuristic judge fallback.
/// </summary>
public sealed record RagEvalCase
{
    /// <summary>
    /// Conventional tag marking a seeded "hard retrieval" case that plain vector
    /// similarity is EXPECTED to miss (corrective-RAG target, RAG-06). CI
    /// regression gates exclude cases carrying this tag from their aggregates.
    /// </summary>
    public const string CorrectiveTag = "correctif";

    /// <summary>Unique case identifier within the dataset (e.g. <c>q-001</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The question submitted to the pipeline under evaluation.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// References of the relevant sources/chunks: a chunk id, a document id, a
    /// source id, or a source path suffix (e.g. <c>corpus/faq.md</c>); an optional
    /// <c>#fragment</c> is ignored by suffix matching. Empty means the case carries
    /// no retrieval ground truth — its retrieval metrics stay <c>null</c>.
    /// </summary>
    public ImmutableList<string> Relevant { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// Substrings the answer text must contain (case-insensitive) — the
    /// deterministic answer-relevance signal of the heuristic judge.
    /// </summary>
    public ImmutableList<string> ExpectedSubstrings { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Optional reference answer handed to the LLM judge.</summary>
    public string? ReferenceAnswer { get; init; }

    /// <summary>Free-form tags (e.g. <c>facile</c>, <c>factuel</c>, <see cref="CorrectiveTag"/>).</summary>
    public ImmutableList<string> Tags { get; init; } = ImmutableList<string>.Empty;
}
