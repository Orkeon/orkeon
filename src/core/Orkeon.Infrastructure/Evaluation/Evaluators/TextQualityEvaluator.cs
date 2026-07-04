using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Base;

namespace Orkeon.Infrastructure.Evaluation.Evaluators;

/// <summary>
/// Typed input for text quality evaluation.
/// </summary>
public sealed record TextQualityInput
{
    /// <summary>Gets the output text to evaluate.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Gets the minimum number of words expected for a good score.</summary>
    public int MinWords { get; init; } = 20;

    /// <summary>Gets the maximum number of words before the score starts declining.</summary>
    public int MaxWords { get; init; } = 5000;
}

/// <summary>
/// Typed result for text quality evaluation.
/// </summary>
public sealed record TextQualityResult
{
    /// <summary>Gets the total word count in the output.</summary>
    public int WordCount { get; init; }

    /// <summary>Gets the word count component score (0.0 to 1.0).</summary>
    public double WordCountScore { get; init; }

    /// <summary>Gets the total sentence count in the output.</summary>
    public int SentenceCount { get; init; }

    /// <summary>Gets the sentence variety component score (0.0 to 1.0).</summary>
    public double SentenceVarietyScore { get; init; }

    /// <summary>Gets the vocabulary richness component score (0.0 to 1.0).</summary>
    public double VocabularyRichnessScore { get; init; }

    /// <summary>Gets the repetition-avoidance component score (0.0 to 1.0).</summary>
    public double RepetitionScore { get; init; }

    /// <summary>Gets the weighted overall quality score (0.0 to 1.0).</summary>
    public double OverallScore { get; init; }

    /// <summary>Gets the human-readable reasoning for the score.</summary>
    public string Reasoning { get; init; } = string.Empty;
}

/// <summary>
/// Heuristic text quality evaluator that measures:
/// - Word count (penalizes very short or very long texts)
/// - Sentence variety (standard deviation of sentence lengths)
/// - Vocabulary richness (unique words / total words)
/// - Repetition detection (fraction of non-repeated sentences)
/// Score is a weighted average of sub-metrics, 0.0 to 1.0.
/// </summary>
public sealed class TextQualityEvaluator : EvaluatorBase<TextQualityInput, TextQualityResult>
{
    private static readonly char[] s_whitespaceChars = [' ', '\t', '\n', '\r'];
    private static readonly char[] s_sentenceDelimiters = ['.', '!', '?'];

    /// <inheritdoc />
    public override string Name => "TextQuality";

    /// <inheritdoc />
    public override string Description => "Heuristic text quality: word count, sentence variety, vocabulary richness, repetition.";

    /// <summary>
    /// Minimum word count for a "good" text (below this, word count score drops).
    /// </summary>
    public int MinWords { get; init; } = 20;

    /// <summary>
    /// Maximum word count for a "good" text (above this, word count score drops).
    /// </summary>
    public int MaxWords { get; init; } = 5000;

    /// <inheritdoc />
    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        return new Dictionary<string, object?>
        {
            ["output"] = input.Output,
            ["min_words"] = MinWords,
            ["max_words"] = MaxWords
        };
    }

    /// <inheritdoc />
    protected override Task<TextQualityResult> ExecuteTypedAsync(TextQualityInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Output))
        {
            return Task.FromResult(new TextQualityResult
            {
                OverallScore = 0.0,
                Reasoning = "Output is empty."
            });
        }

        var words = Tokenize(request.Output);
        var sentences = SplitSentences(request.Output);

        var wordCountScore = ComputeWordCountScore(words.Length, request.MinWords, request.MaxWords);
        var varietyScore = ComputeSentenceVarietyScore(sentences);
        var richnessScore = ComputeVocabularyRichness(words);
        var repetitionScore = ComputeRepetitionScore(sentences);

        // Weighted average: vocabulary richness and repetition are weighted higher.
        var overall = wordCountScore * 0.2
                      + varietyScore * 0.2
                      + richnessScore * 0.3
                      + repetitionScore * 0.3;

        overall = Math.Clamp(overall, 0.0, 1.0);

        return Task.FromResult(new TextQualityResult
        {
            WordCount = words.Length,
            WordCountScore = Math.Round(wordCountScore, 4),
            SentenceCount = sentences.Length,
            SentenceVarietyScore = Math.Round(varietyScore, 4),
            VocabularyRichnessScore = Math.Round(richnessScore, 4),
            RepetitionScore = Math.Round(repetitionScore, 4),
            OverallScore = Math.Round(overall, 4),
            Reasoning = $"Word count: {words.Length}, Sentences: {sentences.Length}"
        });
    }

    /// <inheritdoc />
    protected override double ExtractScore(TextQualityResult result) => result.OverallScore;

    /// <inheritdoc />
    protected override string? ExtractReasoning(TextQualityResult result) => result.Reasoning;

    private static double ComputeWordCountScore(int wordCount, int minWords, int maxWords)
    {
        if (wordCount < minWords)
            return (double)wordCount / minWords;
        if (wordCount > maxWords)
            return Math.Max(0.0, 1.0 - (wordCount - maxWords) / (double)maxWords);
        return 1.0;
    }

    private static double ComputeSentenceVarietyScore(string[] sentences)
    {
        if (sentences.Length < 2)
            return 0.5; // Can't measure variety with fewer than 2 sentences.

        var lengths = sentences.Select(s => (double)Tokenize(s).Length).ToArray();
        var mean = lengths.Average();
        if (mean < 1) return 0.5;

        var variance = lengths.Select(l => (l - mean) * (l - mean)).Average();
        var cv = Math.Sqrt(variance) / mean; // Coefficient of variation

        // CV between 0.3 and 1.0 is ideal; below 0.1 means all sentences are same length.
        return Math.Clamp(cv / 0.5, 0.0, 1.0);
    }

    private static double ComputeVocabularyRichness(string[] words)
    {
        if (words.Length == 0) return 0.0;

        var unique = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
        var ratio = (double)unique.Count / words.Length;

        // Typical ratio for good text is 0.4-0.8; adjust to 0..1 scale.
        return Math.Clamp(ratio, 0.0, 1.0);
    }

    private static double ComputeRepetitionScore(string[] sentences)
    {
        if (sentences.Length < 2) return 1.0;

        var normalized = sentences
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (normalized.Length == 0) return 1.0;

        var unique = new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);
        return (double)unique.Count / normalized.Length;
    }

    private static string[] Tokenize(string text)
    {
        return text.Split(s_whitespaceChars, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] SplitSentences(string text)
    {
        return text.Split(s_sentenceDelimiters, StringSplitOptions.RemoveEmptyEntries)
                   .Where(s => s.Trim().Length > 0)
                   .ToArray();
    }
}
