using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class FormatComplianceEvaluatorTestsFixture
{
    private readonly FormatComplianceEvaluator _evaluator = new();

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public FormatComplianceEvaluator GetEvaluator() => _evaluator;
}
