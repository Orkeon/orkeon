using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class EvaluationSuiteTestsFixture
{
    public static IEvaluator CreateMockEvaluator(string name, double score)
    {
        var mock = new MockEvaluator
        {
            Name = name,
            RequiresLlm = false
        };
        mock.SetScore(score, "Mock evaluation");
        return mock;
    }

    public static EvaluationSuite CreateSuite(string name, IEvaluator[] evaluators)
        => new(name, evaluators);

    public static Task<EvaluationReport> RunAsync(EvaluationSuite suite, List<EvaluationInput> inputs, CancellationToken ct = default)
        => suite.RunAsync(inputs, ct);
}
