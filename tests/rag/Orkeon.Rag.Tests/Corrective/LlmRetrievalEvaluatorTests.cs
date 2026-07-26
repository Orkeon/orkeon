using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// Tests for <see cref="LlmRetrievalEvaluator"/>: the constrained JSON call
/// (temperature 0, <see cref="ChatResponseFormat.Json"/>), the tolerant parsing
/// (fenced/prose-wrapped JSON, synonyms, bare grade words), and the safe
/// <see cref="RetrievalGrade.Ambiguous"/> fallback — a malformed LLM output
/// never throws.
/// </summary>
public class LlmRetrievalEvaluatorTests
{
    private static ScoredChunk Scored(string id, string content = "some content") => new()
    {
        Chunk = new Chunk { Id = id, DocumentId = "d1", SourceId = "s1", Content = content },
        Score = 0.9,
    };

    private static async Task<RetrievalVerdict> EvaluateAsync(string responseText)
    {
        using var chat = new FakeChatClient { ResponseText = responseText };
        var evaluator = new LlmRetrievalEvaluator(chat);
        return await evaluator.EvaluateAsync(
            "what is the warranty period?", [Scored("c1"), Scored("c2")],
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ValidJson_ParsesGradeReasonAndChunkRelevances()
    {
        var verdict = await EvaluateAsync(
            """{"grade": "correct", "reason": "passages answer the question", "chunks": [{"id": "c1", "relevance": 0.9}, {"id": "c2", "relevance": 0.2}]}""");

        Assert.Equal(RetrievalGrade.Correct, verdict.Grade);
        Assert.Equal("passages answer the question", verdict.Rationale);
        Assert.Equal(2, verdict.ChunkRelevances.Count);
        Assert.Equal("c1", verdict.ChunkRelevances[0].ChunkId);
        Assert.Equal(0.9, verdict.ChunkRelevances[0].Relevance);
    }

    [Fact]
    public async Task FencedJson_WithProseAround_StillParses()
    {
        var verdict = await EvaluateAsync(
            "Sure! Here is my assessment:\n```json\n{\"grade\": \"incorrect\", \"reason\": \"off topic\"}\n```\nHope this helps.");

        Assert.Equal(RetrievalGrade.Incorrect, verdict.Grade);
        Assert.Equal("off topic", verdict.Rationale);
    }

    [Theory]
    [InlineData("{\"grade\": \"RELEVANT\"}", RetrievalGrade.Correct)]
    [InlineData("{\"grade\": \"irrelevant\"}", RetrievalGrade.Incorrect)]
    [InlineData("{\"grade\": \"partial\"}", RetrievalGrade.Ambiguous)]
    [InlineData("{\"verdict\": \"ambiguous\"}", RetrievalGrade.Ambiguous)]
    public async Task GradeSynonyms_AndAlternativePropertyNames_AreAccepted(
        string response, RetrievalGrade expected)
    {
        var verdict = await EvaluateAsync(response);

        Assert.Equal(expected, verdict.Grade);
    }

    [Fact]
    public async Task BareGradeWord_WithoutAnyJson_StillParses()
    {
        var verdict = await EvaluateAsync("The retrieval looks ambiguous to me.");

        Assert.Equal(RetrievalGrade.Ambiguous, verdict.Grade);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"grade\": \"banana\"}")]
    [InlineData("{ this is not json }{ neither is this")]
    [InlineData("I cannot help with that request whatsoever")]
    public async Task UnusableResponse_FallsBackToAmbiguous_NeverThrows(string response)
    {
        var verdict = await EvaluateAsync(response);

        Assert.Equal(RetrievalGrade.Ambiguous, verdict.Grade);
        Assert.Contains("unusable", verdict.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyChunks_GradesIncorrectDeterministically_WithoutAnyLlmCall()
    {
        using var chat = new FakeChatClient();
        var evaluator = new LlmRetrievalEvaluator(chat);

        var verdict = await evaluator.EvaluateAsync(
            "any question", [], TestContext.Current.CancellationToken);

        Assert.Equal(RetrievalGrade.Incorrect, verdict.Grade);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task Call_IsConstrained_TemperatureZeroAndJsonResponseFormat()
    {
        using var chat = new FakeChatClient { ResponseText = "{\"grade\": \"correct\"}" };
        var evaluator = new LlmRetrievalEvaluator(chat);

        await evaluator.EvaluateAsync(
            "q?", [Scored("c1", "chunk text")], TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastOptions);
        Assert.Equal(0f, chat.LastOptions.Temperature);
        Assert.Same(ChatResponseFormat.Json, chat.LastOptions.ResponseFormat);
        Assert.Equal(LlmRetrievalEvaluator.SystemPrompt, chat.LastMessages![0].Text);
        Assert.Contains("Question: q?", chat.LastMessages[1].Text, StringComparison.Ordinal);
        Assert.Contains("id: c1", chat.LastMessages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChunkRelevances_TolerateNumericStrings_AndClampOutOfRangeValues()
    {
        var verdict = await EvaluateAsync(
            """{"grade": "ambiguous", "chunks": [{"id": "c1", "relevance": "0.75"}, {"id": "c2", "relevance": 7}]}""");

        Assert.Equal(0.75, verdict.ChunkRelevances[0].Relevance);
        Assert.Equal(1.0, verdict.ChunkRelevances[1].Relevance);
    }
}
