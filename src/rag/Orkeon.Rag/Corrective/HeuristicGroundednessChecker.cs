using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Deterministic, zero-LLM <see cref="IGroundednessChecker"/> for offline runs
/// and tests: splits the answer into sentences (citation markers <c>[n]</c>
/// stripped) and checks the lexical coverage of each sentence's tokens (via the
/// BM25 tokenizer) against the union of the context tokens. A sentence is
/// supported when at least <see cref="SentenceCoverageThreshold"/> of its tokens
/// appear in the context; the answer is grounded when the supported fraction
/// reaches <see cref="GroundedThreshold"/>. Unsupported sentences are returned
/// as the unsupported claims.
/// </summary>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class HeuristicGroundednessChecker : IGroundednessChecker
{
    /// <summary>Default fraction of a sentence's tokens that must appear in the context.</summary>
    public const double DefaultSentenceCoverageThreshold = 0.6;

    /// <summary>Default fraction of supported sentences at (or above) which the answer is grounded.</summary>
    public const double DefaultGroundedThreshold = 0.5;

    /// <summary>Fraction of a sentence's tokens that must appear in the context for it to count as supported.</summary>
    public double SentenceCoverageThreshold { get; }

    /// <summary>Fraction of supported sentences at (or above) which the answer is grounded.</summary>
    public double GroundedThreshold { get; }

    /// <summary>Creates the checker.</summary>
    /// <param name="sentenceCoverageThreshold">Per-sentence token coverage threshold, in [0, 1].</param>
    /// <param name="groundedThreshold">Supported-sentence fraction threshold, in [0, 1].</param>
    public HeuristicGroundednessChecker(
        double sentenceCoverageThreshold = DefaultSentenceCoverageThreshold,
        double groundedThreshold = DefaultGroundedThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sentenceCoverageThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sentenceCoverageThreshold, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(groundedThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(groundedThreshold, 1.0);
        SentenceCoverageThreshold = sentenceCoverageThreshold;
        GroundedThreshold = groundedThreshold;
    }

    /// <inheritdoc />
    public Task<GroundednessResult> CheckAsync(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var sentences = SplitSentences(CitationMarkerRegex().Replace(answer, " "));
        if (sentences.Count == 0)
        {
            // Nothing claimed — vacuously grounded.
            return Task.FromResult(new GroundednessResult
            {
                IsGrounded = true,
                Score = 1.0,
                Rationale = "empty answer — nothing to verify",
            });
        }

        var contextTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scored in context)
            contextTokens.UnionWith(Bm25Index.Tokenize(scored.Chunk.Content));

        var unsupported = ImmutableList.CreateBuilder<string>();
        var supportedCount = 0;
        foreach (var sentence in sentences)
        {
            var tokens = Bm25Index.Tokenize(sentence);
            if (tokens.Count == 0)
            {
                supportedCount++;
                continue;
            }

            var matched = tokens.Count(contextTokens.Contains);
            if ((double)matched / tokens.Count >= SentenceCoverageThreshold)
                supportedCount++;
            else
                unsupported.Add(sentence);
        }

        var score = (double)supportedCount / sentences.Count;
        return Task.FromResult(new GroundednessResult
        {
            IsGrounded = score >= GroundedThreshold,
            Score = score,
            UnsupportedClaims = unsupported.ToImmutable(),
            Rationale = string.Create(
                CultureInfo.InvariantCulture,
                $"{supportedCount}/{sentences.Count} sentences lexically supported (sentence coverage ≥ {SentenceCoverageThreshold:F2})"),
        });
    }

    /// <summary>Splits <paramref name="text"/> into trimmed, non-empty sentences on <c>.</c> <c>!</c> <c>?</c> and newlines.</summary>
    internal static List<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        foreach (var part in text.Split(['.', '!', '?', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                sentences.Add(trimmed);
        }

        return sentences;
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex CitationMarkerRegex();
}
