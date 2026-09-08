using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Roles survive the trip to an OpenAI-compatible provider.
/// </summary>
/// <remarks>
/// <para>
/// A plain conversation — no tools, no images — used to be flattened by
/// <c>HttpLlmProviderBase.ChatAsync</c> into ONE user message shaped
/// <c>"{role}: {content}"</c> per line. A system instruction therefore arrived as part of the
/// user's words, and an assistant turn arrived as something the user claimed the assistant had
/// said. The structured path that builds the correct payload already existed; it was only
/// reachable by declaring a tool or attaching an image.
/// </para>
/// <para>
/// Measured on 2026-08-04 against a live provider: every one of the seven RAG
/// generations went out as
/// <c>messages: [{ role: "user", content: "system: You are a retrieval-augmented assistant…" }]</c>.
/// That is the shape of the whole RAG subsystem's generation stage, and of every LLM judge,
/// retrieval evaluator and groundedness checker in the codebase — none of which declares a
/// tool, so none of which ever took the correct path.
/// </para>
/// <para>
/// Like <see cref="OpenAICompatibleProviderBaseSystemMessageTests"/>, these tests read the
/// outgoing payload. Around 400 mocked provider tests assert on the RESPONSE, which is why a
/// defect this large lived in the request builder undisturbed.
/// </para>
/// </remarks>
public class OpenAICompatibleProviderBaseRoleFidelityTests
{
    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly string OkResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1 },
    });

    /// <summary>Runs one chat call and returns the payload actually sent.</summary>
    private async Task<JsonElement> CapturePayloadAsync(LlmConfig config, LlmMessage[] messages)
    {
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        using var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(config, _httpClientFactory, _noOpPolicy);
        await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.CapturedRequests);
        Assert.NotNull(request.Content);
        var body = await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static (string Role, string Content) At(JsonElement payload, int index)
    {
        var m = payload.GetProperty("messages")[index];
        return (m.GetProperty("role").GetString() ?? "", m.GetProperty("content").GetString() ?? "");
    }

    private static LlmConfig PlainConfig() => LlmConfig.Create(TestModelName, TestApiKey);

    [Fact]
    public async Task ShouldSendSystemAndUserAsTwoRoledMessages_WithNoToolsInvolved()
    {
        // The exact shape of a RAG generation: one system prompt, one user turn.
        var payload = await CapturePayloadAsync(PlainConfig(), [
            LlmMessage.System("You are a retrieval-augmented assistant."),
            LlmMessage.User("Context:\n[1] …\n\nQuestion: what?"),
        ]);

        Assert.Equal(2, payload.GetProperty("messages").GetArrayLength());
        Assert.Equal(("system", "You are a retrieval-augmented assistant."), At(payload, 0));
        Assert.Equal("user", At(payload, 1).Role);
        // The regression itself: the user turn must not begin with the system text.
        Assert.DoesNotContain("system: ", At(payload, 1).Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldPreserveAMultiTurnHistory_InsteadOfConcatenatingIt()
    {
        var payload = await CapturePayloadAsync(PlainConfig(), [
            LlmMessage.User("What is 2 + 2?"),
            LlmMessage.Assistant("4"),
            LlmMessage.User("And multiplied by 3?"),
        ]);

        Assert.Equal(3, payload.GetProperty("messages").GetArrayLength());
        Assert.Equal(("user", "What is 2 + 2?"), At(payload, 0));
        Assert.Equal(("assistant", "4"), At(payload, 1));
        Assert.Equal(("user", "And multiplied by 3?"), At(payload, 2));
    }

    [Fact]
    public async Task ShouldSendALoneSystemMessageAsASystemMessage()
    {
        var payload = await CapturePayloadAsync(PlainConfig(), [LlmMessage.System("Be brief.")]);

        Assert.Equal(("system", "Be brief."), At(payload, 0));
    }

    [Fact]
    public async Task ShouldNotGlueTheRolePrefixOntoALoneUserMessage()
    {
        // This is the case that settled how far the fix should go. The intent was
        // to leave a single user turn on the old path, on the assumption that the
        // two builders produced the same bytes for it. They did not: the old path
        // sent `"user: Hello."` — the role prefix glued onto the user's own words,
        // for every single-message ChatAsync caller. Nothing about the flattening
        // was ever the better payload, so nothing uses it any more.
        var payload = await CapturePayloadAsync(PlainConfig(), [LlmMessage.User("Hello.")]);

        Assert.Equal(1, payload.GetProperty("messages").GetArrayLength());
        Assert.Equal(("user", "Hello."), At(payload, 0));
    }

    [Fact]
    public async Task ShouldStillCarrySamplingOptions_OnTheMultiTurnPath()
    {
        // The two builders emitted slightly different option sets, so routing more
        // callers through the chat builder could have dropped one silently. `grammar`
        // was the only gap and is now written by both.
        var config = PlainConfig() with
        {
            TopP = 0.5,
            StopSequences = ["STOP"],
            GrammarGbnf = "root ::= \"ok\"",
        };

        var payload = await CapturePayloadAsync(config, [
            LlmMessage.System("Be brief."),
            LlmMessage.User("Hello."),
        ]);

        Assert.Equal(0.5, payload.GetProperty("top_p").GetDouble());
        Assert.Equal("STOP", payload.GetProperty("stop")[0].GetString());
        Assert.Equal("root ::= \"ok\"", payload.GetProperty("grammar").GetString());
    }

    [Fact]
    public async Task ShouldStillCarrySamplingOptions_OnTheSinglePromptPath()
    {
        var config = PlainConfig() with { GrammarGbnf = "root ::= \"ok\"" };

        var payload = await CapturePayloadAsync(config, [LlmMessage.User("Hello.")]);

        Assert.Equal("root ::= \"ok\"", payload.GetProperty("grammar").GetString());
    }
}
