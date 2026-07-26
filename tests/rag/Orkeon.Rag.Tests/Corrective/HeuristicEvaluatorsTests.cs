using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// Tests for the deterministic offline evaluators
/// (<see cref="HeuristicRetrievalEvaluator"/>,
/// <see cref="HeuristicGroundednessChecker"/>): lexical-coverage grading and
/// sentence-support verification — zero LLM, reproducible by construction.
/// </summary>
public class HeuristicEvaluatorsTests
{
    private static ScoredChunk Scored(string id, string content) => new()
    {
        Chunk = new Chunk { Id = id, DocumentId = "d1", SourceId = "s1", Content = content },
        Score = 0.9,
    };

    // ── HeuristicRetrievalEvaluator ────────────────────────────────────────

    [Fact]
    public async Task Retrieval_FullLexicalCoverage_GradesCorrect_WithPerChunkRelevances()
    {
        var evaluator = new HeuristicRetrievalEvaluator();

        var verdict = await evaluator.EvaluateAsync(
            "warranty period years",
            [Scored("c1", "The warranty period is two years."), Scored("c2", "unrelated dog text")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RetrievalGrade.Correct, verdict.Grade);
        Assert.Equal(2, verdict.ChunkRelevances.Count);
        Assert.Equal(1.0, verdict.ChunkRelevances[0].Relevance);
        Assert.Equal(0.0, verdict.ChunkRelevances[1].Relevance);
    }

    [Fact]
    public async Task Retrieval_NoLexicalOverlap_GradesIncorrect()
    {
        var evaluator = new HeuristicRetrievalEvaluator();

        var verdict = await evaluator.EvaluateAsync(
            "warranty period",
            [Scored("c1", "bananas are yellow"), Scored("c2", "dogs bark loudly")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RetrievalGrade.Incorrect, verdict.Grade);
    }

    [Fact]
    public async Task Retrieval_PartialCoverage_GradesAmbiguous()
    {
        var evaluator = new HeuristicRetrievalEvaluator();

        // 2 of 5 distinct query tokens covered → 0.4, between 0.2 and 0.6.
        var verdict = await evaluator.EvaluateAsync(
            "alpha bravo charlie delta echo",
            [Scored("c1", "alpha bravo something else entirely")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RetrievalGrade.Ambiguous, verdict.Grade);
    }

    [Fact]
    public async Task Retrieval_EmptyChunks_GradesIncorrect()
    {
        var evaluator = new HeuristicRetrievalEvaluator();

        var verdict = await evaluator.EvaluateAsync(
            "any question", [], TestContext.Current.CancellationToken);

        Assert.Equal(RetrievalGrade.Incorrect, verdict.Grade);
    }

    [Fact]
    public async Task Retrieval_IsDeterministic_SameInputSameVerdict()
    {
        var evaluator = new HeuristicRetrievalEvaluator();
        ScoredChunk[] chunks = [Scored("c1", "alpha bravo charlie")];

        var first = await evaluator.EvaluateAsync(
            "alpha bravo", chunks, TestContext.Current.CancellationToken);
        var second = await evaluator.EvaluateAsync(
            "alpha bravo", chunks, TestContext.Current.CancellationToken);

        Assert.Equal(first.Grade, second.Grade);
        Assert.Equal(
            first.ChunkRelevances.Select(r => r.Relevance),
            second.ChunkRelevances.Select(r => r.Relevance));
    }

    [Fact]
    public void Retrieval_InvalidThresholds_FailLoudly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HeuristicRetrievalEvaluator(correctThreshold: 0.3, incorrectThreshold: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HeuristicRetrievalEvaluator(correctThreshold: 1.5));
    }

    // ── HeuristicGroundednessChecker ───────────────────────────────────────

    [Fact]
    public async Task Groundedness_AnswerCopiedFromTheSources_IsGrounded()
    {
        var checker = new HeuristicGroundednessChecker();

        var result = await checker.CheckAsync(
            "what is the warranty period?",
            "The warranty period is two years [1].",
            [Scored("c1", "The warranty period is two years for all products.")],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsGrounded);
        Assert.Equal(1.0, result.Score);
        Assert.Empty(result.UnsupportedClaims);
    }

    [Fact]
    public async Task Groundedness_FabricatedSentences_AreListedAsUnsupportedClaims()
    {
        var checker = new HeuristicGroundednessChecker();

        var result = await checker.CheckAsync(
            "what is the warranty period?",
            "The warranty period is two years [1]. Purple unicorns guarantee refunds forever.",
            [Scored("c1", "The warranty period is two years for all products.")],
            TestContext.Current.CancellationToken);

        Assert.Equal(0.5, result.Score);
        var claim = Assert.Single(result.UnsupportedClaims);
        Assert.Contains("unicorns", claim, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Groundedness_MostlyFabricatedAnswer_IsUngrounded()
    {
        var checker = new HeuristicGroundednessChecker();

        var result = await checker.CheckAsync(
            "q?",
            "Purple unicorns exist. Dragons audit refunds. Elves sign warranties.",
            [Scored("c1", "The warranty period is two years.")],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsGrounded);
        Assert.Equal(0.0, result.Score);
        Assert.Equal(3, result.UnsupportedClaims.Count);
    }

    [Fact]
    public async Task Groundedness_CitationMarkers_DoNotAffectTheVerdict()
    {
        var checker = new HeuristicGroundednessChecker();

        var result = await checker.CheckAsync(
            "q?",
            "[1] The warranty period is two years [2][3].",
            [Scored("c1", "The warranty period is two years.")],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsGrounded);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task Groundedness_EmptyAnswer_IsVacuouslyGrounded()
    {
        var checker = new HeuristicGroundednessChecker();

        var result = await checker.CheckAsync(
            "q?", "  \n ", [Scored("c1", "anything")], TestContext.Current.CancellationToken);

        Assert.True(result.IsGrounded);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void Groundedness_InvalidThresholds_FailLoudly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HeuristicGroundednessChecker(sentenceCoverageThreshold: 1.2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HeuristicGroundednessChecker(groundedThreshold: -0.1));
    }
}
