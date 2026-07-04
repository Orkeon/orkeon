using Orkeon.Application.Evaluation;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Evaluation.Base;

namespace Orkeon.Infrastructure.Evaluation.Evaluators;

/// <summary>
/// Typed input for similarity evaluation.
/// </summary>
public sealed record SimilarityInput
{
    /// <summary>Gets the output text to evaluate.</summary>
    public string Output { get; init; } = string.Empty;
    /// <summary>Gets the expected output for comparison.</summary>
    public string? ExpectedOutput { get; init; }
}

/// <summary>
/// Typed result for similarity evaluation.
/// </summary>
public sealed record SimilarityResult
{
    /// <summary>Gets the normalized Levenshtein similarity (0.0–1.0).</summary>
    public double LevenshteinSimilarity { get; init; }
    /// <summary>Gets the Jaccard similarity on word sets (0.0–1.0).</summary>
    public double JaccardSimilarity { get; init; }
    /// <summary>Gets the bigram overlap score (0.0–1.0).</summary>
    public double BigramOverlap { get; init; }
    /// <summary>Gets the weighted overall similarity score (0.0–1.0).</summary>
    public double OverallScore { get; init; }
    /// <summary>Gets the human-readable reasoning string.</summary>
    public string Reasoning { get; init; } = string.Empty;
}

/// <summary>
/// Compares output to expected output using multiple similarity metrics:
/// - Levenshtein distance (normalized)
/// - Jaccard similarity on word sets
/// - BLEU-like n-gram overlap (bigram)
/// Score is a weighted average, 0.0 to 1.0.
/// </summary>
public sealed class SimilarityEvaluator : EvaluatorBase<SimilarityInput, SimilarityResult>
{
    private static readonly char[] s_whitespaceChars = [' ', '\t', '\n', '\r'];

    /// <inheritdoc />
    public override string Name => "Similarity";
    /// <inheritdoc />
    public override string Description => "Compares output to expected output using Levenshtein, Jaccard, and n-gram overlap.";

    /// <inheritdoc />
    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        return new Dictionary<string, object?>
        {
            ["output"] = input.Output,
            ["expected_output"] = input.ExpectedOutput ?? string.Empty
        };
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(SimilarityInput request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExpectedOutput))
            return null; // Will be handled as skip in ExecuteTypedAsync
        if (string.IsNullOrWhiteSpace(request.Output))
            return null; // Will be handled as 0.0 in ExecuteTypedAsync
        return null;
    }

    /// <inheritdoc />
    protected override Task<SimilarityResult> ExecuteTypedAsync(SimilarityInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExpectedOutput))
        {
            return Task.FromResult(new SimilarityResult
            {
                OverallScore = 1.0,
                Reasoning = "No expected output provided; skipping similarity check."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Output))
        {
            return Task.FromResult(new SimilarityResult
            {
                OverallScore = 0.0,
                Reasoning = "Output is empty."
            });
        }

        var output = request.Output;
        var expected = request.ExpectedOutput;

        var levenshtein = NormalizedLevenshteinSimilarity(output, expected);
        var jaccard = ComputeJaccardSimilarity(output, expected);
        var ngram = ComputeBigramOverlap(output, expected);

        // Weighted: Jaccard and n-gram more important than raw edit distance.
        var overall = levenshtein * 0.2 + jaccard * 0.4 + ngram * 0.4;
        overall = Math.Clamp(overall, 0.0, 1.0);

        return Task.FromResult(new SimilarityResult
        {
            LevenshteinSimilarity = Math.Round(levenshtein, 4),
            JaccardSimilarity = Math.Round(jaccard, 4),
            BigramOverlap = Math.Round(ngram, 4),
            OverallScore = Math.Round(overall, 4),
            Reasoning = Inv.Format($"Levenshtein={levenshtein:F3}, Jaccard={jaccard:F3}, Bigram={ngram:F3}")
        });
    }

    /// <inheritdoc />
    protected override double ExtractScore(SimilarityResult result) => result.OverallScore;

    /// <inheritdoc />
    protected override string? ExtractReasoning(SimilarityResult result) => result.Reasoning;

    /// <summary>Computes the normalized Levenshtein similarity between two strings.</summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns>Similarity score between 0.0 and 1.0.</returns>
    public static double NormalizedLevenshteinSimilarity(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a == b) return 1.0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;

        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1][];
        for (var i = 0; i <= n; i++) d[i] = new int[m + 1];

        for (var i = 0; i <= n; i++) d[i][0] = i;
        for (var j = 0; j <= m; j++) d[0][j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i][j] = Math.Min(
                    Math.Min(d[i - 1][j] + 1, d[i][j - 1] + 1),
                    d[i - 1][j - 1] + cost);
            }
        }

        return d[n][m];
    }

    /// <summary>Computes the Jaccard similarity on word sets between two strings.</summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns>Similarity score between 0.0 and 1.0.</returns>
    public static double JaccardSimilarity(string a, string b)
    {
        return ComputeJaccardSimilarity(a, b);
    }

    private static double ComputeJaccardSimilarity(string a, string b)
    {
        var wordsA = Tokenize(a);
        var wordsB = Tokenize(b);

        if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;

        var intersection = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
        var union = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>Computes the bigram overlap between two strings.</summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns>Overlap score between 0.0 and 1.0.</returns>
    public static double BigramOverlap(string a, string b)
    {
        return ComputeBigramOverlap(a, b);
    }

    private static double ComputeBigramOverlap(string a, string b)
    {
        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);

        if (bigramsA.Count == 0 && bigramsB.Count == 0) return 1.0;
        if (bigramsA.Count == 0 || bigramsB.Count == 0) return 0.0;

        var intersection = bigramsA.Intersect(bigramsB, StringComparer.OrdinalIgnoreCase).Count();
        var union = bigramsA.Union(bigramsB, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> GetBigrams(string text)
    {
        var words = text.Split(s_whitespaceChars, StringSplitOptions.RemoveEmptyEntries);
        var bigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < words.Length - 1; i++)
        {
            bigrams.Add($"{words[i]} {words[i + 1]}");
        }
        return bigrams;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return new HashSet<string>(
            text.Split(s_whitespaceChars, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
    }
}
