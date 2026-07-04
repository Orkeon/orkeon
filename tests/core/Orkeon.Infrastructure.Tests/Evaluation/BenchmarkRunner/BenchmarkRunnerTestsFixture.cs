using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class BenchmarkRunnerTestsFixture
{
    private readonly BenchmarkRunner _runner = new();

    public static IEvaluator CreateMockEvaluator(string name, double score)
    {
        var mock = new MockEvaluator();
        mock.Name = name;
        mock.RequiresLlm = false;
        mock.SetScore(score, "Mock");
        return mock;
    }

    public static MockEvaluator CreateMockEvaluatorWithFunc(string name, Func<EvaluationInput, EvaluationScore> evaluateFunc)
    {
        var mock = new MockEvaluator();
        mock.Name = name;
        mock.RequiresLlm = false;
        mock.SetEvaluateFunc(evaluateFunc);
        return mock;
    }

    public static MockEvaluator CreateLlmEvaluator(string name, double score, string reasoning)
    {
        var mock = new MockEvaluator();
        mock.Name = name;
        mock.RequiresLlm = true;
        mock.SetScore(score, reasoning);
        return mock;
    }

    public static EvaluationSuite CreateSuite(string name, IEvaluator[] evaluators)
        => new(name, evaluators);

    public static InMemoryDataset CreateDataset(string name, EvaluationInput[] inputs)
        => new(name, inputs);

    public static BenchmarkConfig CreateConfig(IEvaluationDataset dataset, EvaluationSuite suite, int runsPerCase = 1)
        => new(dataset, suite, RunsPerCase: runsPerCase);

    public Task<BenchmarkReport> RunAsync(BenchmarkConfig config)
        => _runner.RunAsync(config);

    public BenchmarkRunner GetRunner() => _runner;
}
