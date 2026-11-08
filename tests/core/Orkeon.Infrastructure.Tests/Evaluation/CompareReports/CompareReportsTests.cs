using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class CompareReportsTests
{
    private readonly CompareReportsTestsFixture _fixture = new();

    [Fact]
    public void ShouldDetectNoRegression_WhenScoresAreEqual()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["Eval"] = 0.8 });
        var current = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["Eval"] = 0.8 });

        var result = CompareReportsTestsFixture.Compare(baseline, current);

        Assert.False(result.HasRegression);
        Assert.Equal(0.0, result.ScoreDelta, 4);
    }

    [Fact]
    public void ShouldDetectRegression_WhenScoreDropExceedsThreshold()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["Eval"] = 0.8 });
        var current = CompareReportsTestsFixture.CreateReport(0.7, new Dictionary<string, double> { ["Eval"] = 0.7 });

        var result = CompareReportsTestsFixture.Compare(baseline, current, regressionThreshold: 0.05);

        Assert.True(result.HasRegression);
        Assert.Equal(-0.1, result.ScoreDelta, 4);
    }

    [Fact]
    public void ShouldDetectNoRegression_WhenScoreDropIsBelowThreshold()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.80, new Dictionary<string, double> { ["Eval"] = 0.80 });
        var current = CompareReportsTestsFixture.CreateReport(0.78, new Dictionary<string, double> { ["Eval"] = 0.78 });

        var result = CompareReportsTestsFixture.Compare(baseline, current, regressionThreshold: 0.05);

        Assert.False(result.HasRegression);
    }

    [Fact]
    public void ShouldDetectImprovement_WhenScoreIncreases()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.7, new Dictionary<string, double> { ["Eval"] = 0.7 });
        var current = CompareReportsTestsFixture.CreateReport(0.9, new Dictionary<string, double> { ["Eval"] = 0.9 });

        var result = CompareReportsTestsFixture.Compare(baseline, current, regressionThreshold: 0.05);

        Assert.False(result.HasRegression);
        Assert.True(result.ScoreDelta > 0);
        Assert.True(result.EvaluatorComparisons[0].IsImprovement);
    }

    [Fact]
    public void ShouldIdentifyRegression_WhenComparingPerEvaluator()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double>
        {
            ["A"] = 0.9,
            ["B"] = 0.7
        });
        var current = CompareReportsTestsFixture.CreateReport(0.7, new Dictionary<string, double>
        {
            ["A"] = 0.5,
            ["B"] = 0.9
        });

        var result = CompareReportsTestsFixture.Compare(baseline, current, regressionThreshold: 0.05);

        Assert.True(result.HasRegression);

        var compA = result.EvaluatorComparisons.First(c => c.EvaluatorName == "A");
        Assert.True(compA.IsRegression);
        Assert.False(compA.IsImprovement);

        var compB = result.EvaluatorComparisons.First(c => c.EvaluatorName == "B");
        Assert.False(compB.IsRegression);
        Assert.True(compB.IsImprovement);
    }

    [Fact]
    public void ShouldCalculateDeltaCorrectly_WhenComparing()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.6, new Dictionary<string, double> { ["X"] = 0.6 });
        var current = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["X"] = 0.8 });

        var result = CompareReportsTestsFixture.Compare(baseline, current);

        Assert.Equal(0.2, result.ScoreDelta, 4);
        Assert.Equal(0.6, result.BaselineScore, 4);
        Assert.Equal(0.8, result.CurrentScore, 4);
    }

    [Fact]
    public void ShouldRefuse_WhenAnEvaluatorIsPresentOnOneSideOnly()
    {
        // Counted as 0 on the missing side, an evaluator scored by one report alone used to read as a
        // regression (or an improvement) the reports never measured.
        var baseline = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["A"] = 0.8, ["B"] = 0.9 });
        var current = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["A"] = 0.8 });

        var ex = Assert.Throws<InvalidOperationException>(() => CompareReportsTestsFixture.Compare(baseline, current));

        Assert.Contains("evaluators", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(CompareReports.Incompatibility(current, baseline));
    }

    [Fact]
    public void ShouldRefuse_WhenTheReportsScoreDifferentDatasets()
    {
        var baseline = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["A"] = 0.8 });
        var current = baseline with { DatasetName = "Other" };

        var ex = Assert.Throws<InvalidOperationException>(() => CompareReportsTestsFixture.Compare(baseline, current));

        Assert.Contains("'Other'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ShouldRefuseAThresholdOutOfRange(double threshold)
    {
        var report = CompareReportsTestsFixture.CreateReport(0.8, new Dictionary<string, double> { ["A"] = 0.8 });

        Assert.Throws<ArgumentOutOfRangeException>(() => CompareReportsTestsFixture.Compare(report, report, threshold));
        Assert.False(CompareReports.IsValidThreshold(threshold));
    }
}
