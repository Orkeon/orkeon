using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// How the generation metrics (groundedness, answer-relevance) were produced.
/// The mode is ALWAYS labelled — per case and per report — so a report never
/// leaves any ambiguity about what was measured (plan §9.2).
/// </summary>
public enum RagJudgeMode
{
    /// <summary>
    /// Deterministic heuristic: expected substrings found in the answer text +
    /// citation coverage of the relevant sources. Zero network, CI-safe.
    /// </summary>
    Heuristic,

    /// <summary>LLM-as-judge through <c>Microsoft.Extensions.AI.IChatClient</c>.</summary>
    Llm,
}

/// <summary>
/// Per-case outcome of an evaluation run. Retrieval metrics are deterministic
/// (relevant refs vs retrieved citations); generation metrics carry the judge
/// mode that actually produced them.
/// </summary>
public sealed record RagEvalCaseResult
{
    /// <summary>Identifier of the evaluated <see cref="RagEvalCase"/>.</summary>
    public required string CaseId { get; init; }

    /// <summary>Tags copied from the case (gates filter on them, e.g. <see cref="RagEvalCase.CorrectiveTag"/>).</summary>
    public ImmutableList<string> Tags { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Source ids of the retrieved citations, in rank order.</summary>
    public ImmutableList<string> RetrievedSources { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Recall@K — fraction of relevant refs surfaced in the top K. <c>null</c> when the case has no <c>relevant</c> ground truth.</summary>
    public double? RecallAtK { get; init; }

    /// <summary>Precision@K — fraction of the top K slots holding a relevant item (denominator K). <c>null</c> without ground truth.</summary>
    public double? PrecisionAtK { get; init; }

    /// <summary>Reciprocal rank of the first relevant retrieved item (0 when none). <c>null</c> without ground truth.</summary>
    public double? ReciprocalRank { get; init; }

    /// <summary>Groundedness in [0, 1]. <c>null</c> when not measurable (heuristic mode without ground truth).</summary>
    public double? Groundedness { get; init; }

    /// <summary>Answer-relevance in [0, 1]. <c>null</c> when not measurable (heuristic mode without expected substrings).</summary>
    public double? AnswerRelevance { get; init; }

    /// <summary>Judge that actually produced the generation metrics of THIS case.</summary>
    public required RagJudgeMode Judge { get; init; }

    /// <summary>Answer text returned by the pipeline (kept for the report).</summary>
    public string? Answer { get; init; }

    /// <summary>Wall-clock duration of the pipeline query for this case.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Aggregated metrics of a run: arithmetic means over the cases whose metric is
/// not <c>null</c>. A metric with no contributing case stays <c>null</c>.
/// </summary>
public sealed record RagEvalAggregate
{
    /// <summary>Number of evaluated cases.</summary>
    public int CaseCount { get; init; }

    /// <summary>Mean recall@K.</summary>
    public double? RecallAtK { get; init; }

    /// <summary>Mean precision@K.</summary>
    public double? PrecisionAtK { get; init; }

    /// <summary>Mean reciprocal rank (MRR).</summary>
    public double? Mrr { get; init; }

    /// <summary>Mean groundedness.</summary>
    public double? Groundedness { get; init; }

    /// <summary>Mean answer-relevance.</summary>
    public double? AnswerRelevance { get; init; }
}

/// <summary>
/// Outcome of an <see cref="Interfaces.IRagEvaluator"/> run: per-case lines plus
/// aggregates, stamped with the evaluated profile and the labelled judge mode.
/// </summary>
public sealed record RagEvalReport
{
    /// <summary>Name of the evaluated dataset.</summary>
    public required string DatasetName { get; init; }

    /// <summary>Profile name the run was resolved with.</summary>
    public required string Profile { get; init; }

    /// <summary>Collection the run queried.</summary>
    public required string Collection { get; init; }

    /// <summary>Metric cutoff (the K of recall@K / precision@K).</summary>
    public int K { get; init; }

    /// <summary>
    /// Judge mode SELECTED for the run. When <see cref="RagJudgeMode.Llm"/> and
    /// <see cref="JudgeFallbackCount"/> is non-zero, that many cases individually
    /// fell back to the heuristic (each case carries its actual mode).
    /// </summary>
    public required RagJudgeMode Judge { get; init; }

    /// <summary>Number of cases that fell back to the heuristic judge inside an LLM-judged run.</summary>
    public int JudgeFallbackCount { get; init; }

    /// <summary>Per-case results, in dataset order.</summary>
    public ImmutableList<RagEvalCaseResult> Cases { get; init; } = ImmutableList<RagEvalCaseResult>.Empty;

    /// <summary>Aggregated metrics over <see cref="Cases"/>.</summary>
    public required RagEvalAggregate Aggregate { get; init; }

    /// <summary>UTC start of the run.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Total wall-clock duration of the run.</summary>
    public TimeSpan Duration { get; init; }
}
