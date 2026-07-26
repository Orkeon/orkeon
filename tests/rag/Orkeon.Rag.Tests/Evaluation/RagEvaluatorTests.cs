using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// The evaluator over hand-written doubles: retrieval metrics from citations,
/// profile-hook resolution, and the judge-mode labelling contract (llm vs
/// heuristic — never ambiguous).
/// </summary>
public sealed class RagEvaluatorTests
{
    private static RagEvalDataset Dataset(params RagEvalCase[] cases) => new()
    {
        Name = "test",
        Cases = [.. cases],
    };

    private static RagEvalCase RefundCase() => new()
    {
        Id = "q-001",
        Question = "refund policy?",
        Relevant = ["corpus/faq.md"],
        ExpectedSubstrings = ["30 days"],
        Tags = ["facile"],
    };

    private static RagAnswer RefundAnswer() => new()
    {
        Text = "Refunds are allowed within 30 days [1].",
        Citations =
        [
            new Citation
            {
                Marker = 1,
                ChunkId = "faq#0",
                SourceId = "/workspace/corpus/faq.md",
                DocumentId = "faq",
                Score = 0.9,
            },
        ],
    };

    private static (RagEvaluator Evaluator, FakeRagPipeline Pipeline, RecordingProfileResolver Resolver)
        Build(FakeChatClient? chatClient = null)
    {
        var pipeline = new FakeRagPipeline();
        var resolver = new RecordingProfileResolver { Pipeline = pipeline };
        return (new RagEvaluator(resolver, chatClient), pipeline, resolver);
    }

    [Fact]
    public async Task Run_ComputesRetrievalMetricsFromCitations_AndAggregates()
    {
        var (evaluator, pipeline, _) = Build();
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs" },
            TestContext.Current.CancellationToken);

        var result = Assert.Single(report.Cases);
        Assert.Equal(1.0, result.RecallAtK);          // the single relevant ref is retrieved at rank 1
        Assert.Equal(1.0 / 5.0, result.PrecisionAtK); // 1 hit / k=5 slots
        Assert.Equal(1.0, result.ReciprocalRank);
        Assert.Equal(1.0, result.AnswerRelevance);    // "30 days" present in the answer
        Assert.Equal(1.0, result.Groundedness);       // the citation covers the relevant ref
        Assert.Equal(["/workspace/corpus/faq.md"], result.RetrievedSources);

        Assert.Equal(1, report.Aggregate.CaseCount);
        Assert.Equal(1.0, report.Aggregate.RecallAtK);
        Assert.Equal(1.0, report.Aggregate.Mrr);

        var query = Assert.Single(pipeline.Queries);
        Assert.Equal("docs", query.Collection);
        Assert.Equal(5, query.TopN); // max(TopN=5, K=5)
    }

    [Fact]
    public async Task Run_ResolvesThePipelineThroughTheProfileHook_AndStampsTheProfile()
    {
        var (evaluator, pipeline, resolver) = Build();
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs", Profile = "fast" },
            TestContext.Current.CancellationToken);

        Assert.Equal("fast", report.Profile);
        Assert.Equal(["fast"], resolver.ResolvedProfiles);
    }

    [Fact]
    public async Task Run_WithoutLlmJudgeRequested_LabelsHeuristic()
    {
        using var chatClient = new FakeChatClient();
        var (evaluator, pipeline, _) = Build(chatClient);
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs", UseLlmJudge = false },
            TestContext.Current.CancellationToken);

        Assert.Equal(RagJudgeMode.Heuristic, report.Judge);
        Assert.Equal(0, report.JudgeFallbackCount);
        Assert.All(report.Cases, c => Assert.Equal(RagJudgeMode.Heuristic, c.Judge));
    }

    [Fact]
    public async Task Run_WithLlmJudgeRequested_ButNoChatClient_FallsBackToHeuristic_Labelled()
    {
        var (evaluator, pipeline, _) = Build(chatClient: null);
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs", UseLlmJudge = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(RagJudgeMode.Heuristic, report.Judge);
        Assert.All(report.Cases, c => Assert.Equal(RagJudgeMode.Heuristic, c.Judge));
    }

    [Fact]
    public async Task Run_WithLlmJudge_UsesTheVerdictScores_AndLabelsLlm()
    {
        using var chatClient = new FakeChatClient
        {
            ResponseText = """{"groundedness": 0.9, "answer_relevance": 0.8}""",
        };
        var (evaluator, pipeline, _) = Build(chatClient);
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs", UseLlmJudge = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(RagJudgeMode.Llm, report.Judge);
        Assert.Equal(0, report.JudgeFallbackCount);
        var result = Assert.Single(report.Cases);
        Assert.Equal(RagJudgeMode.Llm, result.Judge);
        Assert.Equal(0.9, result.Groundedness);
        Assert.Equal(0.8, result.AnswerRelevance);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task Run_WithLlmJudge_UnparsableVerdict_FallsBackPerCase_AndCountsTheFallback()
    {
        using var chatClient = new FakeChatClient { ResponseText = "sorry, I cannot rate this" };
        var (evaluator, pipeline, _) = Build(chatClient);
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();

        var report = await evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = "docs", UseLlmJudge = true },
            TestContext.Current.CancellationToken);

        // Selected mode stays llm; the case is labelled heuristic and counted.
        Assert.Equal(RagJudgeMode.Llm, report.Judge);
        Assert.Equal(1, report.JudgeFallbackCount);
        var result = Assert.Single(report.Cases);
        Assert.Equal(RagJudgeMode.Heuristic, result.Judge);
        Assert.Equal(1.0, result.AnswerRelevance); // deterministic fallback values
    }

    [Fact]
    public async Task Run_CaseWithoutGroundTruth_HasNullRetrievalMetrics_ExcludedFromAggregates()
    {
        var (evaluator, pipeline, _) = Build();
        pipeline.AnswersByQuestion["refund policy?"] = RefundAnswer();
        pipeline.DefaultAnswer = new RagAnswer { Text = "no idea" };

        var report = await evaluator.RunAsync(
            Dataset(
                RefundCase(),
                new RagEvalCase { Id = "q-open", Question = "open question?" }),
            new RagEvalOptions { Collection = "docs" },
            TestContext.Current.CancellationToken);

        var open = Assert.Single(report.Cases, c => c.CaseId == "q-open");
        Assert.Null(open.RecallAtK);
        Assert.Null(open.PrecisionAtK);
        Assert.Null(open.ReciprocalRank);

        // Aggregates average only the contributing case.
        Assert.Equal(2, report.Aggregate.CaseCount);
        Assert.Equal(1.0, report.Aggregate.RecallAtK);
    }

    [Fact]
    public async Task Run_RejectsEmptyDatasets_AndBlankCollections()
    {
        var (evaluator, _, _) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => evaluator.RunAsync(
            Dataset(),
            new RagEvalOptions { Collection = "docs" },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => evaluator.RunAsync(
            Dataset(RefundCase()),
            new RagEvalOptions { Collection = " " },
            TestContext.Current.CancellationToken));
    }
}
