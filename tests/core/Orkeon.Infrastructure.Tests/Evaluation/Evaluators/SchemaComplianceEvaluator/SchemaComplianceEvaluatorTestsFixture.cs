using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Evaluators;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class SchemaComplianceEvaluatorTestsFixture
{
    private readonly SchemaComplianceEvaluator _evaluator = new();

    public Task<EvaluationScore> EvaluateAsync(EvaluationInput input)
        => _evaluator.EvaluateAsync(input);

    public SchemaComplianceEvaluator GetEvaluator() => _evaluator;

    public static string SchemaMetadataKey => SchemaComplianceEvaluator.SchemaMetadataKey;
}
