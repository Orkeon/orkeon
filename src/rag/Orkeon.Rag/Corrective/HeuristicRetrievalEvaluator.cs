using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Deterministic, zero-LLM <see cref="IRetrievalEvaluator"/> for offline runs
/// and tests: grades the retrieved chunk set by lexical coverage of the query —
/// the fraction of distinct query tokens (via the BM25 tokenizer) present in
/// each chunk. The best per-chunk coverage decides the grade:
/// <c>≥ CorrectThreshold</c> → <see cref="RetrievalGrade.Correct"/>,
/// <c>&lt; IncorrectThreshold</c> → <see cref="RetrievalGrade.Incorrect"/>,
/// in between → <see cref="RetrievalGrade.Ambiguous"/>.
/// </summary>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class HeuristicRetrievalEvaluator : IRetrievalEvaluator
{
    /// <summary>Default coverage at (or above) which the retrieval is graded Correct.</summary>
    public const double DefaultCorrectThreshold = 0.6;

    /// <summary>Default coverage below which the retrieval is graded Incorrect.</summary>
    public const double DefaultIncorrectThreshold = 0.2;

    private readonly double _correctThreshold;
    private readonly double _incorrectThreshold;

    /// <summary>Creates the evaluator.</summary>
    /// <param name="correctThreshold">Coverage in [0, 1] at (or above) which the grade is Correct.</param>
    /// <param name="incorrectThreshold">Coverage in [0, 1] below which the grade is Incorrect (must not exceed <paramref name="correctThreshold"/>).</param>
    public HeuristicRetrievalEvaluator(
        double correctThreshold = DefaultCorrectThreshold,
        double incorrectThreshold = DefaultIncorrectThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(incorrectThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(correctThreshold, 1.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(incorrectThreshold, correctThreshold);
        _correctThreshold = correctThreshold;
        _incorrectThreshold = incorrectThreshold;
    }

    /// <inheritdoc />
    public Task<RetrievalVerdict> EvaluateAsync(
        string query,
        IReadOnlyList<ScoredChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();

        if (chunks.Count == 0)
        {
            return Task.FromResult(new RetrievalVerdict
            {
                Grade = RetrievalGrade.Incorrect,
                Rationale = "no chunks retrieved",
            });
        }

        var queryTokens = Bm25Index.Tokenize(query).ToHashSet(StringComparer.Ordinal);
        if (queryTokens.Count == 0)
        {
            // Nothing to measure against — uncertain by construction.
            return Task.FromResult(new RetrievalVerdict
            {
                Grade = RetrievalGrade.Ambiguous,
                Rationale = "query has no indexable token",
            });
        }

        var relevances = ImmutableList.CreateBuilder<ChunkRelevance>();
        var best = 0.0;
#pragma warning disable S3267 // single pass accumulating two results (running max + relevance list); Select would need two
        foreach (var scored in chunks)
        {
            var chunkTokens = Bm25Index.Tokenize(scored.Chunk.Content).ToHashSet(StringComparer.Ordinal);
            var matched = queryTokens.Count(chunkTokens.Contains);
            var coverage = (double)matched / queryTokens.Count;
            best = Math.Max(best, coverage);
            relevances.Add(new ChunkRelevance
            {
                ChunkId = scored.Chunk.Id,
                Relevance = coverage,
            });
        }
#pragma warning restore S3267

        var grade = best switch
        {
            _ when best >= _correctThreshold => RetrievalGrade.Correct,
            _ when best < _incorrectThreshold => RetrievalGrade.Incorrect,
            _ => RetrievalGrade.Ambiguous,
        };

        return Task.FromResult(new RetrievalVerdict
        {
            Grade = grade,
            ChunkRelevances = relevances.ToImmutable(),
            Rationale = string.Create(
                CultureInfo.InvariantCulture,
                $"best lexical coverage {best:F2} (correct ≥ {_correctThreshold:F2}, incorrect < {_incorrectThreshold:F2})"),
        });
    }
}
