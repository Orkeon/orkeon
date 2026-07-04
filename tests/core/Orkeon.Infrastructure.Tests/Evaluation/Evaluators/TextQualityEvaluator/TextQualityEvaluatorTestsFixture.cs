using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class TextQualityEvaluatorTestsFixture
{
    private readonly TextQualityEvaluator _evaluator = new();

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public TextQualityEvaluator GetEvaluator() => _evaluator;
}
