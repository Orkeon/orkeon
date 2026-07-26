using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Evaluation;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// The one-call harness over hand-written doubles: corpus ingestion pre-pass
/// (glob expansion, incremental/reindex), default collection naming,
/// multi-profile runs with the comparison table, and the loud empty-corpus
/// failure.
/// </summary>
public sealed class RagEvalHarnessTests
{
    private const string DatasetYaml = """
        name: mini
        corpus: ./corpus
        cases:
          - id: q-1
            question: "what is alpha?"
            relevant: ["corpus/a.md"]
            expected_substrings: ["alpha"]
        """;

    private static FakeFileSystemService BuildFileSystem(bool withCorpus = true)
    {
        var fs = new FakeFileSystemService()
            .AddMount("/workspace", FileAccessRights.ReadOnly)
            .AddMount("/output", FileAccessRights.ReadWrite)
            .AddFile("/workspace/eval/ds.yaml", DatasetYaml);

        if (withCorpus)
        {
            fs.AddFile("/workspace/eval/corpus/a.md", "alpha content");
            fs.AddFile("/workspace/eval/corpus/b.md", "beta content");
        }

        return fs;
    }

    private static (RagEvalHarness Harness, FakeIngestionPipeline Ingestion, FakeRagEvaluator Evaluator, FakeFileSystemService Fs)
        Build(bool withCorpus = true)
    {
        var fs = BuildFileSystem(withCorpus);
        var ingestion = new FakeIngestionPipeline();
        var evaluator = new FakeRagEvaluator();
        var harness = new RagEvalHarness(
            new RagEvalDatasetYamlLoader(fs),
            evaluator,
            ingestion,
            fs,
            new RagEvalReportWriter(fs));
        return (harness, ingestion, evaluator, fs);
    }

    [Fact]
    public async Task Run_IngestsTheCorpus_ThenEvaluatesTheDefaultProfile()
    {
        var (harness, ingestion, evaluator, _) = Build();

        var result = await harness.RunAsync(
            new RagEvalRunRequest { DatasetPath = "/workspace/eval/ds.yaml" },
            TestContext.Current.CancellationToken);

        // Corpus expanded through the VFS glob, into the derived collection name.
        var ingestRequest = Assert.Single(ingestion.Requests);
        Assert.Equal("rag-eval-mini", ingestRequest.Collection);
        Assert.Equal(
            ["/workspace/eval/corpus/a.md", "/workspace/eval/corpus/b.md"],
            ingestRequest.Sources.Select(s => s.Location));
        Assert.False(ingestRequest.Reindex);

        // Single default profile evaluated against the same collection.
        var evalOptions = Assert.Single(evaluator.Calls);
        Assert.Equal("rag-eval-mini", evalOptions.Collection);
        Assert.Equal("default", evalOptions.Profile);
        Assert.Equal(5, evalOptions.K);

        Assert.NotNull(result.Ingestion);
        var report = Assert.Single(result.Reports);
        Assert.Equal("mini", report.DatasetName);
        Assert.Null(result.ComparisonFile);
        Assert.Equal(
            ["/output/rag/eval/mini-default.md", "/output/rag/eval/mini-default.json"],
            result.WrittenFiles);
    }

    [Fact]
    public async Task Run_CompareMode_ProducesOneReportPerProfile_AndTheComparisonTable()
    {
        var (harness, _, evaluator, fs) = Build();

        var result = await harness.RunAsync(
            new RagEvalRunRequest
            {
                DatasetPath = "/workspace/eval/ds.yaml",
                Profiles = ["fast", "balanced", "quality"],
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["fast", "balanced", "quality"], evaluator.Calls.Select(o => o.Profile));
        Assert.Equal(3, result.Reports.Count);
        Assert.Equal(6, result.WrittenFiles.Count);
        Assert.Equal("/output/rag/eval/mini-compare.md", result.ComparisonFile);

        var comparison = await fs.TryReadAllTextAsync(
            result.ComparisonFile!, TestContext.Current.CancellationToken);
        Assert.NotNull(comparison);
        Assert.Contains("| fast |", comparison, StringComparison.Ordinal);
        Assert.Contains("| quality |", comparison, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_PropagatesCollectionOverride_Reindex_K_AndJudgeSelection()
    {
        var (harness, ingestion, evaluator, _) = Build();

        await harness.RunAsync(
            new RagEvalRunRequest
            {
                DatasetPath = "/workspace/eval/ds.yaml",
                Collection = "custom-kb",
                K = 3,
                UseLlmJudge = true,
                ReindexCorpus = true,
            },
            TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(ingestion.Requests).Reindex);
        Assert.Equal("custom-kb", Assert.Single(ingestion.Requests).Collection);
        var options = Assert.Single(evaluator.Calls);
        Assert.Equal("custom-kb", options.Collection);
        Assert.Equal(3, options.K);
        Assert.Equal(5, options.TopN); // max(5, K)
        Assert.True(options.UseLlmJudge);
    }

    [Fact]
    public async Task Run_WithIngestCorpusDisabled_SkipsTheIngestionPrePass()
    {
        var (harness, ingestion, _, _) = Build();

        var result = await harness.RunAsync(
            new RagEvalRunRequest { DatasetPath = "/workspace/eval/ds.yaml", IngestCorpus = false },
            TestContext.Current.CancellationToken);

        Assert.Empty(ingestion.Requests);
        Assert.Null(result.Ingestion);
    }

    [Fact]
    public async Task Run_WithAnEmptyCorpus_FailsLoudly()
    {
        var (harness, _, _, _) = Build(withCorpus: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync(
            new RagEvalRunRequest { DatasetPath = "/workspace/eval/ds.yaml" },
            TestContext.Current.CancellationToken));

        Assert.Contains("matched no file", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/workspace/eval/ds.yaml", "./corpus", "/workspace/eval/corpus")]
    [InlineData("/workspace/eval/ds.yaml", "corpus", "/workspace/eval/corpus")]
    [InlineData("/workspace/eval/ds.yaml", "/data/corpus/", "/data/corpus")]
    public void ResolveCorpusDirectory_HandlesRelativeAndAbsoluteForms(
        string datasetPath, string corpusPath, string expected)
        => Assert.Equal(expected, RagEvalHarness.ResolveCorpusDirectory(datasetPath, corpusPath));
}
