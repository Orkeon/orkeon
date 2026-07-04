using Orkeon.Application.Interfaces.Compliance;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Compliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.Compliance;

public class NistComplianceReportGeneratorTests
{
    [Fact]
    public async Task GenerateReportAsync_ReturnsReport_WithControls()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.NotNull(report.ReportId);
        Assert.NotEmpty(report.Controls);
        Assert.Equal(NistControlMapping.GetAllControls().Count, report.Controls.Count);
    }

    [Fact]
    public async Task GenerateReportAsync_ControlsMarkedImplemented_WhenAuditEventsExist()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture()
            .WithAuditEventsForCategories(
                AuditCategory.LlmCall,       // covers SI, AU, RA
                AuditCategory.ToolExecution); // covers SI, AC, AU
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        // SI family controls should be Implemented (covered by LlmCall and ToolExecution)
        var siControls = report.Controls.Where(c => c.Family == "SI").ToList();
        Assert.All(siControls, c => Assert.Equal(ControlStatus.Implemented, c.Status));

        // AC family controls should be Implemented (covered by ToolExecution)
        var acControls = report.Controls.Where(c => c.Family == "AC").ToList();
        Assert.All(acControls, c => Assert.Equal(ControlStatus.Implemented, c.Status));
    }

    [Fact]
    public async Task GenerateReportAsync_ControlsMarkedNotImplemented_WhenNoAuditEvents()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        Assert.All(report.Controls, c => Assert.Equal(ControlStatus.NotImplemented, c.Status));
    }

    [Fact]
    public async Task GetComplianceScoreAsync_CalculatesScoreCorrectly_WithFullCoverage()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture()
            .WithAllCategoriesCovered();
        var generator = fixture.CreateGenerator();

        var score = await generator.GetComplianceScoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1.0, score.OverallScore, precision: 2);
        Assert.Equal(score.TotalControls, score.ImplementedControls);
        Assert.Equal(0, score.NotImplementedControls);
        Assert.Equal(0, score.PartiallyImplementedControls);
        Assert.Equal(0, score.NotApplicableControls);
    }

    [Fact]
    public async Task GetComplianceScoreAsync_EmptyAuditHistory_ResultsInZeroScore()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var score = await generator.GetComplianceScoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0.0, score.OverallScore);
        Assert.Equal(0, score.ImplementedControls);
        Assert.True(score.NotImplementedControls > 0);
    }

    [Fact]
    public async Task GetComplianceScoreAsync_PartialCoverage_ReturnsIntermediateScore()
    {
        // Only cover a subset of families
        var fixture = new NistComplianceReportGeneratorTestsFixture()
            .WithAuditEventsForCategories(AuditCategory.LlmCall); // covers SI, AU, RA
        var generator = fixture.CreateGenerator();

        var score = await generator.GetComplianceScoreAsync(TestContext.Current.CancellationToken);

        Assert.True(score.OverallScore > 0.0, "Score should be above zero with some coverage");
        Assert.True(score.OverallScore < 1.0, "Score should be below 1.0 with partial coverage");
        Assert.True(score.ImplementedControls > 0);
        Assert.True(score.NotImplementedControls > 0);
    }

    [Fact]
    public async Task GetComplianceScoreAsync_ScoresByFamily_ContainsAllFamilies()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture()
            .WithAllCategoriesCovered();
        var generator = fixture.CreateGenerator();

        var score = await generator.GetComplianceScoreAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(score.ScoresByFamily);
        // All families should have a score entry
        foreach (var family in new[] { "AC", "AU", "CM", "IR", "RA", "SI", "SC", "PM" })
        {
            Assert.True(score.ScoresByFamily.ContainsKey(family),
                $"ScoresByFamily should contain family '{family}'");
            Assert.Equal(1.0, score.ScoresByFamily[family], precision: 2);
        }
    }

    [Fact]
    public async Task GenerateReportAsync_IncludesFindings_ForMissingControls()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Findings);
        // Each NotImplemented control should generate a finding
        var notImplementedCount = report.Controls.Count(c => c.Status == ControlStatus.NotImplemented);
        Assert.Equal(notImplementedCount, report.Findings.Count);
    }

    [Fact]
    public async Task GenerateReportAsync_IncludesRecommendations_ForMissingFamilies()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Recommendations);
    }

    [Fact]
    public async Task GenerateReportAsync_FullCoverage_RecommendationsIndicateCompliance()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture()
            .WithAllCategoriesCovered();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        // When fully covered, there should be a single recommendation about periodic review
        Assert.Single(report.Recommendations);
        Assert.Contains("periodic review", report.Recommendations[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateReportAsync_ReportHasValidTimestamp()
    {
        var before = DateTime.UtcNow;
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);
        var after = DateTime.UtcNow;

        Assert.InRange(report.GeneratedAt, before, after);
    }

    [Fact]
    public async Task GenerateReportAsync_ReportHasNonEmptyId()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        var generator = fixture.CreateGenerator();

        var report = await generator.GenerateReportAsync(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(report.ReportId));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAuditLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NistComplianceReportGenerator(null!, NullLogger<NistComplianceReportGenerator>.Instance));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        var fixture = new NistComplianceReportGeneratorTestsFixture();
        Assert.Throws<ArgumentNullException>(() =>
            new NistComplianceReportGenerator(fixture.GetAuditLogger(), null!));
    }
}
