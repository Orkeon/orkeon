using Microsoft.Extensions.AI;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Corrective;

/// <summary>
/// Tests for <see cref="LlmGroundednessChecker"/> — the first real
/// implementation of the <c>IGroundednessChecker</c> hook: the constrained JSON
/// call, the tolerant parsing (fenced JSON, boolean synonyms, numeric strings),
/// and the safe grounded fallback — a malformed LLM output never throws.
/// </summary>
public class LlmGroundednessCheckerTests
{
    private static ScoredChunk Scored(string id, string content = "warranty is two years") => new()
    {
        Chunk = new Chunk { Id = id, DocumentId = "d1", SourceId = "s1", Content = content },
        Score = 0.9,
    };

    private static async Task<GroundednessResult> CheckAsync(string responseText)
    {
        using var chat = new FakeChatClient { ResponseText = responseText };
        var checker = new LlmGroundednessChecker(chat);
        return await checker.CheckAsync(
            "what is the warranty period?", "Two years [1].", [Scored("c1")],
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ValidJson_ParsesGroundedScoreClaimsAndReason()
    {
        var result = await CheckAsync(
            """{"grounded": false, "score": 0.25, "unsupported_claims": ["ten-year warranty", "free returns"], "reason": "claims not in sources"}""");

        Assert.False(result.IsGrounded);
        Assert.Equal(0.25, result.Score);
        Assert.Equal(["ten-year warranty", "free returns"], result.UnsupportedClaims);
        Assert.Equal("claims not in sources", result.Rationale);
    }

    [Fact]
    public async Task FencedJson_WithProseAround_StillParses()
    {
        var result = await CheckAsync(
            "Here is the verification:\n```json\n{\"grounded\": true, \"score\": 0.9}\n```");

        Assert.True(result.IsGrounded);
        Assert.Equal(0.9, result.Score);
    }

    [Theory]
    [InlineData("{\"grounded\": \"yes\"}", true)]
    [InlineData("{\"grounded\": \"no\"}", false)]
    [InlineData("{\"is_grounded\": true}", true)]
    [InlineData("{\"grounded\": \"UNGROUNDED\"}", false)]
    public async Task BooleanSynonyms_AndAlternativePropertyNames_AreAccepted(
        string response, bool expected)
    {
        var result = await CheckAsync(response);

        Assert.Equal(expected, result.IsGrounded);
    }

    [Fact]
    public async Task ScoreAsNumericString_IsCoerced_AndMissingScoreDefaultsFromTheVerdict()
    {
        var coerced = await CheckAsync("""{"grounded": false, "score": "0.4"}""");
        Assert.Equal(0.4, coerced.Score);

        var defaulted = await CheckAsync("""{"grounded": true}""");
        Assert.Equal(1.0, defaulted.Score);
    }

    [Theory]
    [InlineData("")]
    [InlineData("everything looks fine to me")]
    [InlineData("{\"grounded\": \"maybe\"}")]
    [InlineData("{ broken json")]
    public async Task UnusableResponse_FallsBackToGrounded_NeverThrows(string response)
    {
        var result = await CheckAsync(response);

        Assert.True(result.IsGrounded);
        Assert.Contains("unusable", result.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyContext_IsDeterministic_WithoutAnyLlmCall()
    {
        using var chat = new FakeChatClient();
        var checker = new LlmGroundednessChecker(chat);

        var claiming = await checker.CheckAsync(
            "q?", "a fabricated answer", [], TestContext.Current.CancellationToken);
        Assert.False(claiming.IsGrounded);

        var silent = await checker.CheckAsync(
            "q?", "   ", [], TestContext.Current.CancellationToken);
        Assert.True(silent.IsGrounded);

        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task Call_IsConstrained_TemperatureZeroAndJsonResponseFormat()
    {
        using var chat = new FakeChatClient { ResponseText = "{\"grounded\": true}" };
        var checker = new LlmGroundednessChecker(chat);

        await checker.CheckAsync(
            "q?", "the answer [1]", [Scored("c1")], TestContext.Current.CancellationToken);

        Assert.NotNull(chat.LastOptions);
        Assert.Equal(0f, chat.LastOptions.Temperature);
        Assert.Same(ChatResponseFormat.Json, chat.LastOptions.ResponseFormat);
        Assert.Equal(LlmGroundednessChecker.SystemPrompt, chat.LastMessages![0].Text);
        Assert.Contains("Answer to verify: the answer [1]", chat.LastMessages[1].Text, StringComparison.Ordinal);
    }
}
