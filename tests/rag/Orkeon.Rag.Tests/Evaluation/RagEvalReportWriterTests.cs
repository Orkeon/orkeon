using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// Markdown/JSON rendition of the reports, the judge-mode labelling, the
/// comparison table, and the VFS writes.
/// </summary>
public sealed class RagEvalReportWriterTests
{
    private static RagEvalReport Report(
        string profile = "default",
        RagJudgeMode judge = RagJudgeMode.Heuristic,
        int fallbacks = 0) => new()
    {
        DatasetName = "golden",
        Profile = profile,
        Collection = "kb",
        K = 5,
        Judge = judge,
        JudgeFallbackCount = fallbacks,
        Cases =
        [
            new RagEvalCaseResult
            {
                CaseId = "q-001",
                Tags = ["facile"],
                RecallAtK = 1.0,
                PrecisionAtK = 0.2,
                ReciprocalRank = 1.0,
                Groundedness = 1.0,
                AnswerRelevance = 0.5,
                Judge = judge,
                Duration = TimeSpan.FromMilliseconds(12),
            },
            new RagEvalCaseResult
            {
                CaseId = "q-007",
                Tags = ["correctif"],
                RecallAtK = 0.0,
                PrecisionAtK = 0.0,
                ReciprocalRank = 0.0,
                Judge = judge,
                Duration = TimeSpan.FromMilliseconds(8),
            },
        ],
        Aggregate = new RagEvalAggregate
        {
            CaseCount = 2,
            RecallAtK = 0.5,
            PrecisionAtK = 0.1,
            Mrr = 0.5,
            Groundedness = 1.0,
            AnswerRelevance = 0.5,
        },
        Duration = TimeSpan.FromSeconds(1.5),
    };

    [Fact]
    public void Markdown_CarriesTheJudgeLabel_PerCaseLines_AndTheAggregate()
    {
        var markdown = RagEvalReportWriter.ToMarkdown(Report());

        Assert.Contains("# RAG evaluation — dataset `golden`, profile `default`", markdown, StringComparison.Ordinal);
        Assert.Contains("- Judge: heuristic", markdown, StringComparison.Ordinal);
        Assert.Contains("| q-001 | 1.00 | 0.20 | 1.00 | 1.00 | 0.50 | heuristic | 12 | facile |", markdown, StringComparison.Ordinal);
        Assert.Contains("| q-007 | 0.00 | 0.00 | 0.00 | n/a | n/a | heuristic | 8 | correctif |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Aggregate", markdown, StringComparison.Ordinal);
        Assert.Contains("| 0.50 | 0.10 | 0.50 | 1.00 | 0.50 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void JudgeLabel_SurfacesLlmFallbacks_Unambiguously()
    {
        Assert.Equal("heuristic", RagEvalReportWriter.JudgeLabel(Report()));
        Assert.Equal("llm", RagEvalReportWriter.JudgeLabel(Report(judge: RagJudgeMode.Llm)));
        Assert.Equal(
            "llm (2 heuristic fallback(s))",
            RagEvalReportWriter.JudgeLabel(Report(judge: RagJudgeMode.Llm, fallbacks: 2)));
    }

    [Fact]
    public void Json_IsSnakeCase_WithEnumsAsStrings()
    {
        var json = RagEvalReportWriter.ToJson(Report());

        Assert.Contains("\"dataset_name\": \"golden\"", json, StringComparison.Ordinal);
        Assert.Contains("\"judge\": \"heuristic\"", json, StringComparison.Ordinal);
        Assert.Contains("\"recall_at_k\"", json, StringComparison.Ordinal);
        Assert.Contains("\"case_id\": \"q-001\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonMarkdown_HasOneRowPerProfile()
    {
        var markdown = RagEvalReportWriter.ToComparisonMarkdown(
            [Report("fast"), Report("balanced"), Report("quality")]);

        Assert.Contains("| profile | recall@5 | MRR | groundedness | answer-relevance | judge | ms/case |", markdown, StringComparison.Ordinal);
        Assert.Contains("| fast | 0.50 | 0.50 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| balanced | 0.50 | 0.50 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| quality | 0.50 | 0.50 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_EmitsMarkdownAndJson_ThroughTheVfs_WithStableNames()
    {
        var fs = new FakeFileSystemService().AddMount("/output", FileAccessRights.ReadWrite);
        var writer = new RagEvalReportWriter(fs);
        var ct = TestContext.Current.CancellationToken;

        var files = await writer.WriteAsync(Report(), "/output/rag/eval", ct);

        Assert.Equal(["/output/rag/eval/golden-default.md", "/output/rag/eval/golden-default.json"], files);
        var markdown = await fs.TryReadAllTextAsync("/output/rag/eval/golden-default.md", ct);
        Assert.Equal(RagEvalReportWriter.ToMarkdown(Report()), markdown);

        var comparison = await writer.WriteComparisonAsync([Report("fast"), Report("quality")], "/output/rag/eval", ct);
        Assert.Equal("/output/rag/eval/golden-compare.md", comparison);
        Assert.NotNull(await fs.TryReadAllTextAsync(comparison, ct));
    }
}
