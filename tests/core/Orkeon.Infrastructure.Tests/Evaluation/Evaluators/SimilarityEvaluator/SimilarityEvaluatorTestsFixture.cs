using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class SimilarityEvaluatorTestsFixture
{
    private readonly SimilarityEvaluator _evaluator = new();

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public SimilarityEvaluator GetEvaluator() => _evaluator;

    public static double JaccardSimilarity(string a, string b)
        => SimilarityEvaluator.JaccardSimilarity(a, b);

    public static double BigramOverlap(string a, string b)
        => SimilarityEvaluator.BigramOverlap(a, b);

    public static double NormalizedLevenshteinSimilarity(string a, string b)
        => SimilarityEvaluator.NormalizedLevenshteinSimilarity(a, b);
}
