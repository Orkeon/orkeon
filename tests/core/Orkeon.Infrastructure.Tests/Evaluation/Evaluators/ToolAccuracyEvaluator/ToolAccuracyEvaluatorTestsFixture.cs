using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class ToolAccuracyEvaluatorTestsFixture
{
    private readonly ToolAccuracyEvaluator _evaluator = new();

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public ToolAccuracyEvaluator GetEvaluator() => _evaluator;

    public static string ExpectedToolsKey => ToolAccuracyEvaluator.ExpectedToolsKey;
}
