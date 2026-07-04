using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.Base;

public class EvaluatorBaseTestsFixture
{
    private readonly LengthCheckEvaluator _evaluator = new();

    public EvaluatorBaseTestsFixture WithValidationOverride(string? validationMessage)
    {
        _evaluator.ValidationOverride = validationMessage;
        return this;
    }

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public LengthCheckEvaluator GetEvaluator() => _evaluator;
}
