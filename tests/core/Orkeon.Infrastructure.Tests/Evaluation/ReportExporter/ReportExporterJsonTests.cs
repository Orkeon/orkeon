using System.Text.Json;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

/// <summary>FWK-09 - the JSON form of the reports <c>orkeon eval</c> writes and reads back.</summary>
public class ReportExporterJsonTests
{
    private static EvaluationReport Evaluation() => new(
        "Default",
        new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc),
        [new EvaluationCaseResult(0, "case zero", [new EvaluationScore("Similarity", 0.8, "close")], 0.8)],
        new EvaluationSummary(1, 0.8, new Dictionary<string, double> { ["Similarity"] = 0.8 }, new Dictionary<string, double> { ["Similarity"] = 0.0 }));

    private static BenchmarkReport Benchmark() => new(
        "golden",
        new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc),
        3,
        [new BenchmarkCaseResult(0, [], new Dictionary<string, double> { ["Similarity"] = 0.9 }, new Dictionary<string, double> { ["Similarity"] = 0.01 })],
        new BenchmarkSummary(0.9, 0.0, new Dictionary<string, double> { ["Similarity"] = 0.9 }, new Dictionary<string, double> { ["Similarity"] = 0.0001 }));

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldWriteSnakeCaseProperties_WhenExportingAnEvaluationReport()
    {
        using var doc = JsonDocument.Parse(ReportExporter.ToJson(Evaluation()));

        var root = doc.RootElement;
        Assert.Equal("Default", root.GetProperty("suite_name").GetString());
        Assert.Equal(0.8, root.GetProperty("summary").GetProperty("overall_score").GetDouble());
        var score = root.GetProperty("results")[0].GetProperty("scores")[0];
        Assert.Equal("Similarity", score.GetProperty("evaluator_name").GetString());
        Assert.Equal(0.8, root.GetProperty("summary").GetProperty("scores_by_evaluator").GetProperty("Similarity").GetDouble());
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldRoundTrip_WhenABenchmarkReportIsExportedThenRead()
    {
        var read = ReportExporter.ReadComparableReport(ReportExporter.ToJson(Benchmark()));

        Assert.Equal("golden", read.DatasetName);
        Assert.Equal(3, read.RunsPerCase);
        Assert.Equal(0.9, read.Summary.OverallMean);
        Assert.Equal(0.9, read.Summary.MeanByEvaluator["Similarity"]);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldReadAnEvaluationReportAsAComparableOne()
    {
        var read = ReportExporter.ReadComparableReport(ReportExporter.ToJson(Evaluation()));

        Assert.Equal(0.8, read.Summary.OverallMean);
        Assert.Equal(0.8, read.Summary.MeanByEvaluator["Similarity"]);
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldRefuseJsonThatIsNoReport()
    {
        Assert.Throws<InvalidOperationException>(() => ReportExporter.ReadComparableReport("[1,2]"));
        Assert.Throws<InvalidOperationException>(() => ReportExporter.ReadComparableReport("{\"nothing\":1}"));
    }

    [Fact]
    [Trait("BR", "FWK-09")]
    public void ShouldWriteTheComparison_WithItsRegressionFlag()
    {
        var worse = Benchmark() with { Summary = Benchmark().Summary with { OverallMean = 0.5 } };
        var comparison = CompareReports.Compare(Benchmark(), worse);

        using var doc = JsonDocument.Parse(ReportExporter.ToJson(comparison));

        Assert.True(doc.RootElement.GetProperty("has_regression").GetBoolean());
        Assert.Equal(0.9, doc.RootElement.GetProperty("baseline_score").GetDouble());
    }
}
