using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// The deterministic judge fallback: substring-based answer-relevance, citation
/// coverage groundedness, and the heuristic labelling (plan §9.2).
/// </summary>
public sealed class HeuristicGenerationJudgeTests
{
    private static RagEvalCase Case(
        string[]? expected = null, string[]? relevant = null) => new()
    {
        Id = "q-t",
        Question = "any",
        ExpectedSubstrings = [.. expected ?? []],
        Relevant = [.. relevant ?? []],
    };

    private static Citation CitationFor(string sourceId, int marker = 1) => new()
    {
        Marker = marker,
        ChunkId = $"{sourceId}#0",
        SourceId = sourceId,
    };

    [Fact]
    public async Task AnswerRelevance_IsTheFractionOfExpectedSubstringsPresent_CaseInsensitive()
    {
        var judge = new HeuristicGenerationJudge();
        var answer = new RagAnswer { Text = "Refunds are allowed within 30 DAYS of delivery." };

        var judgement = await judge.JudgeAsync(
            Case(expected: ["30 days", "restocking fee"]), answer, TestContext.Current.CancellationToken);

        Assert.Equal(RagJudgeMode.Heuristic, judgement.Mode);
        Assert.Equal(0.5, judgement.AnswerRelevance);
    }

    [Fact]
    public async Task AnswerRelevance_IsNull_WhenTheCaseDeclaresNoExpectedSubstring()
    {
        var judge = new HeuristicGenerationJudge();

        var judgement = await judge.JudgeAsync(
            Case(), new RagAnswer { Text = "anything" }, TestContext.Current.CancellationToken);

        Assert.Null(judgement.AnswerRelevance);
    }

    [Fact]
    public async Task Groundedness_IsTheCitationCoverageOfTheRelevantRefs()
    {
        var judge = new HeuristicGenerationJudge();
        var answer = new RagAnswer
        {
            Text = "answer",
            Citations = [CitationFor("/workspace/corpus/doc-a.md")],
        };

        var judgement = await judge.JudgeAsync(
            Case(relevant: ["corpus/doc-a.md", "corpus/doc-b.md"]), answer, TestContext.Current.CancellationToken);

        Assert.Equal(0.5, judgement.Groundedness);
    }

    [Fact]
    public async Task Groundedness_IsZeroWithoutCitations_AndNullWithoutGroundTruth()
    {
        var judge = new HeuristicGenerationJudge();

        var noCitations = await judge.JudgeAsync(
            Case(relevant: ["corpus/doc-a.md"]), new RagAnswer { Text = "x" }, TestContext.Current.CancellationToken);
        Assert.Equal(0.0, noCitations.Groundedness);

        var noTruth = await judge.JudgeAsync(
            Case(), new RagAnswer { Text = "x" }, TestContext.Current.CancellationToken);
        Assert.Null(noTruth.Groundedness);
    }

    [Fact]
    public async Task Judgement_IsDeterministic_SameInputsSameScores()
    {
        var judge = new HeuristicGenerationJudge();
        var evalCase = Case(expected: ["30 days"], relevant: ["corpus/doc-a.md"]);
        var answer = new RagAnswer
        {
            Text = "Within 30 days.",
            Citations = [CitationFor("/x/corpus/doc-a.md")],
        };

        var first = await judge.JudgeAsync(evalCase, answer, TestContext.Current.CancellationToken);
        var second = await judge.JudgeAsync(evalCase, answer, TestContext.Current.CancellationToken);

        Assert.Equal(first.AnswerRelevance, second.AnswerRelevance);
        Assert.Equal(first.Groundedness, second.Groundedness);
        Assert.Equal(RagJudgeMode.Heuristic, first.Mode);
        Assert.Equal(RagJudgeMode.Heuristic, second.Mode);
    }
}
