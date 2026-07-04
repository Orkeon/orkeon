using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class EvaluationSuiteTests
{
    private readonly EvaluationSuiteTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnAllScores_WhenRunningWithMultipleEvaluators()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("Eval1", 0.8);
        var eval2 = EvaluationSuiteTestsFixture.CreateMockEvaluator("Eval2", 0.6);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1, eval2]);

        var inputs = new List<EvaluationInput>
        {
            new("output text", ExpectedOutput: "expected text")
        };

        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, inputs, TestContext.Current.CancellationToken);

        Assert.Equal("TestSuite", report.SuiteName);
        Assert.Single(report.Results);
        Assert.Equal(2, report.Results[0].Scores.Count);
        Assert.Equal("Eval1", report.Results[0].Scores[0].EvaluatorName);
        Assert.Equal("Eval2", report.Results[0].Scores[1].EvaluatorName);
    }

    [Fact]
    public async Task ShouldCalculateAveragesCorrectly_WhenRunning()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("A", 0.8);
        var eval2 = EvaluationSuiteTestsFixture.CreateMockEvaluator("B", 0.4);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1, eval2]);

        var inputs = new List<EvaluationInput> { new("output") };

        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, inputs, TestContext.Current.CancellationToken);

        Assert.Equal(0.6, report.Results[0].AverageScore, 4);
        Assert.Equal(0.6, report.Summary.OverallScore, 4);
    }

    [Fact]
    public async Task ShouldHandleEmptyInputList_WhenRunning()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("Eval1", 0.8);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1]);

        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, [], TestContext.Current.CancellationToken);

        Assert.Empty(report.Results);
        Assert.Equal(0, report.Summary.TotalCases);
        Assert.Equal(0.0, report.Summary.OverallScore);
    }

    [Fact]
    public async Task ShouldComputeSummary_WhenRunningMultipleCases()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("A", 0.9);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1]);

        var inputs = new List<EvaluationInput>
        {
            new("output1"),
            new("output2"),
            new("output3")
        };

        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, inputs, TestContext.Current.CancellationToken);

        Assert.Equal(3, report.Summary.TotalCases);
        Assert.Equal(3, report.Results.Count);
        Assert.Equal(0.9, report.Summary.OverallScore, 4);
    }

    [Fact]
    public void ShouldReturnNewSuiteWithEvaluator_WhenAddingEvaluator()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("A", 0.8);
        var eval2 = EvaluationSuiteTestsFixture.CreateMockEvaluator("B", 0.6);

        var suite1 = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1]);
        var suite2 = suite1.AddEvaluator(eval2);

        Assert.Single(suite1.Evaluators);
        Assert.Equal(2, suite2.Evaluators.Count);
    }

    [Fact]
    public async Task ShouldContainScoresByEvaluator_WhenAccessingSummary()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("Eval1", 0.9);
        var eval2 = EvaluationSuiteTestsFixture.CreateMockEvaluator("Eval2", 0.5);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1, eval2]);

        var inputs = new List<EvaluationInput> { new("output") };
        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, inputs, TestContext.Current.CancellationToken);

        Assert.True(report.Summary.ScoresByEvaluator.ContainsKey("Eval1"));
        Assert.True(report.Summary.ScoresByEvaluator.ContainsKey("Eval2"));
        Assert.Equal(0.9, report.Summary.ScoresByEvaluator["Eval1"], 4);
        Assert.Equal(0.5, report.Summary.ScoresByEvaluator["Eval2"], 4);
    }

    [Fact]
    public async Task ShouldContainVariance_WhenAccessingSummary()
    {
        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("A", 0.8);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1]);

        var inputs = new List<EvaluationInput> { new("output") };
        var report = await EvaluationSuiteTestsFixture.RunAsync(suite, inputs, TestContext.Current.CancellationToken);

        Assert.True(report.Summary.Variance.ContainsKey("A"));
    }

    [Fact]
    public async Task ShouldRespectCancellation_WhenTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var eval1 = EvaluationSuiteTestsFixture.CreateMockEvaluator("A", 0.8);
        var suite = EvaluationSuiteTestsFixture.CreateSuite("TestSuite", [eval1]);
        var inputs = new List<EvaluationInput> { new("output") };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => EvaluationSuiteTestsFixture.RunAsync(suite, inputs, cts.Token));
    }
}
