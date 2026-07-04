using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class BenchmarkRunnerTests
{
    private readonly BenchmarkRunnerTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldRunMultipleTimes_WhenRunsPerCaseIsSet()
    {
        var evaluator = BenchmarkRunnerTestsFixture.CreateMockEvaluator("Eval", 0.8);
        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [evaluator]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("TestData", [new EvaluationInput("output1")]);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 3);

        var report = await _fixture.RunAsync(config);

        Assert.Equal("TestData", report.DatasetName);
        Assert.Equal(3, report.RunsPerCase);
        Assert.Single(report.Results);
        Assert.Equal(3, report.Results[0].RunResults.Count);
    }

    [Fact]
    public async Task ShouldCalculateMeanAndStdDev_WhenScoresVary()
    {
        var callCount = 0;
        var scores = new[] { 0.7, 0.8, 0.9 };
        var mock = BenchmarkRunnerTestsFixture.CreateMockEvaluatorWithFunc("Varying", input =>
        {
            var idx = Interlocked.Increment(ref callCount) - 1;
            var score = scores[idx % scores.Length];
            return new EvaluationScore("Varying", score, "test");
        });

        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [(IEvaluator)mock]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("Data", [new EvaluationInput("output")]);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 3);

        var report = await _fixture.RunAsync(config);

        Assert.True(report.Results[0].MeanScores.ContainsKey("Varying"));
        Assert.True(report.Results[0].StdDevScores.ContainsKey("Varying"));

        var mean = report.Results[0].MeanScores["Varying"];
        Assert.InRange(mean, 0.79, 0.81);

        var stdDev = report.Results[0].StdDevScores["Varying"];
        Assert.True(stdDev > 0.0, "Standard deviation should be > 0 for varying scores");
    }

    [Fact]
    public async Task ShouldProducePerCaseResults_WhenMultipleCasesExist()
    {
        var evaluator = BenchmarkRunnerTestsFixture.CreateMockEvaluator("Eval", 0.9);
        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [evaluator]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("Data",
        [
            new EvaluationInput("output1"),
            new EvaluationInput("output2")
        ]);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 2);

        var report = await _fixture.RunAsync(config);

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(0, report.Results[0].CaseIndex);
        Assert.Equal(1, report.Results[1].CaseIndex);
    }

    [Fact]
    public async Task ShouldContainOverallStats_WhenAccessingSummary()
    {
        var evaluator = BenchmarkRunnerTestsFixture.CreateMockEvaluator("Eval", 0.75);
        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [evaluator]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("Data", [new EvaluationInput("output")]);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 1);

        var report = await _fixture.RunAsync(config);

        Assert.Equal(0.75, report.Summary.OverallMean, 2);
        Assert.True(report.Summary.MeanByEvaluator.ContainsKey("Eval"));
    }

    [Fact]
    public async Task ShouldIncludeLlmEvaluators_WhenNoClientIsAvailable()
    {
        var deterministicEval = BenchmarkRunnerTestsFixture.CreateMockEvaluator("Deterministic", 0.9);
        var llmEval = BenchmarkRunnerTestsFixture.CreateLlmEvaluator("LlmJudge", 0.0, "No LLM available");

        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [deterministicEval, llmEval]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("Data", [new EvaluationInput("output")]);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 1);

        var report = await _fixture.RunAsync(config);

        Assert.Equal(2, report.Results[0].RunResults[0].Scores.Count);
    }

    [Fact]
    public async Task ShouldProduceEmptyReport_WhenDatasetIsEmpty()
    {
        var evaluator = BenchmarkRunnerTestsFixture.CreateMockEvaluator("Eval", 0.8);
        var suite = BenchmarkRunnerTestsFixture.CreateSuite("Suite", [evaluator]);
        var dataset = BenchmarkRunnerTestsFixture.CreateDataset("Empty", []);
        var config = BenchmarkRunnerTestsFixture.CreateConfig(dataset, suite, runsPerCase: 3);

        var report = await _fixture.RunAsync(config);

        Assert.Empty(report.Results);
        Assert.Equal(0.0, report.Summary.OverallMean);
    }
}
