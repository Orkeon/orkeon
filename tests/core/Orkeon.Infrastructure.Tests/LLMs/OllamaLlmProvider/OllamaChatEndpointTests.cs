using System.Net;
using System.Text.Json;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Ollama's <c>/api/chat</c> path (LLM-07, G-10): native tool calling and image input, plus
/// the guarantee that plain conversations keep the historical prompt-completion path.
/// </summary>
public class OllamaChatEndpointTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<OllamaLlmProvider> _logger = new();

    private OllamaLlmProvider CreateProvider(LlmConfig config, TestHttpMessageHandler handler)
    {
        _httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient(handler));
        return new OllamaLlmProvider(
            config,
            _httpClientFactory,
            new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance),
            _logger);
    }

    private static LlmConfig BaseConfig() =>
        LlmConfig.Create("llama3.2") with { BaseUrl = new Uri("http://localhost:11434") };

    private static LlmConfig WithTools(LlmConfig config) => config with
    {
        Tools = [new ToolSchema("get_weather", "Get the weather", [])],
    };

    private static async Task<JsonElement> ReadRequestAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // ── Endpoint selection ──────────────────────────────────────────────────

    /// <summary>
    /// The migration must not move existing behaviour: a plain conversation with no tools and
    /// no images keeps the prompt-completion path it has always used.
    /// </summary>
    [Fact]
    public async Task ShouldStayOnGenerate_ForAPlainConversation()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"response":"hi","done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("/api/generate", handler.CapturedRequests.Single().RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSwitchToChat_WhenToolsAreDeclared()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"hi"},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        await provider.ChatAsync(
            [LlmMessage.User("weather?")], cancellationToken: TestContext.Current.CancellationToken);

        var request = handler.CapturedRequests.Single();
        Assert.EndsWith("/api/chat", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var payload = await ReadRequestAsync(request);
        Assert.Equal("get_weather",
            payload.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    // ── Native tool calling (the actual reason for the migration) ───────────

    /// <summary>
    /// Ollama returns <c>message.tool_calls</c> with <c>arguments</c> as a JSON object and no
    /// <c>choices</c> array — the two differences that made the OpenAI parser unable to read
    /// it, and the reason tool calling went through the text fallback.
    /// </summary>
    [Fact]
    public async Task ShouldReshapeToolCalls_IntoTheOpenAiBodyTheParserReads()
    {
        var ollamaResponse = JsonSerializer.Serialize(new
        {
            message = new
            {
                role = "assistant",
                content = "",
                tool_calls = new[]
                {
                    new { function = new { name = "get_weather", arguments = new { city = "Paris" } } },
                },
            },
            done = true,
            prompt_eval_count = 12,
            eval_count = 8,
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, ollamaResponse);
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var response = await provider.ChatAsync(
            [LlmMessage.User("weather?")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.RawResponseBody);
        using var body = JsonDocument.Parse(response.RawResponseBody!);
        var call = body.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("tool_calls")[0];

        Assert.Equal("get_weather", call.GetProperty("function").GetProperty("name").GetString());
        // arguments must be a JSON *string*, which is what the OpenAI protocol specifies.
        var arguments = call.GetProperty("function").GetProperty("arguments");
        Assert.Equal(JsonValueKind.String, arguments.ValueKind);
        Assert.Contains("Paris", arguments.GetString()!, StringComparison.Ordinal);
        // A tool result must be correlatable back to its call.
        Assert.False(string.IsNullOrWhiteSpace(call.GetProperty("id").GetString()));

        Assert.Equal(12, response.PromptTokens);
        Assert.Equal(8, response.CompletionTokens);
        Assert.Equal(20, response.TokensUsed);
    }

    /// <summary>
    /// A model that calls no tool must leave the raw body null, so the text-fallback protocol
    /// stays in charge for models without tool support.
    /// </summary>
    [Fact]
    public async Task ShouldLeaveTheRawBodyNull_WhenNoToolWasCalled()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"just prose"},"done":true}""");
        using var provider = CreateProvider(WithTools(BaseConfig()), handler);

        var response = await provider.ChatAsync(
            [LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("just prose", response.Content);
        Assert.Null(response.RawResponseBody);
    }

    /// <summary>The framework stores OpenAI-shaped tool calls; Ollama wants objects back.</summary>
    [Fact]
    public async Task ShouldConvertArgumentsBackToAnObject_WhenReplayingAnAssistantTurn()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var assistantTurn = LlmMessage.Assistant("") with
        {
            RawToolCalls = """[{"id":"c1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}]""",
        };

        var toolResult = new LlmMessage { Role = LlmRoles.Tool, Content = "sunny", ToolCallId = "c1" };

        await provider.ChatAsync(
            [LlmMessage.User("weather?"), assistantTurn, toolResult],
            cancellationToken: TestContext.Current.CancellationToken);

        var payload = await ReadRequestAsync(handler.CapturedRequests.Single());
        var arguments = payload.GetProperty("messages")[1]
            .GetProperty("tool_calls")[0]
            .GetProperty("function")
            .GetProperty("arguments");

        Assert.Equal(JsonValueKind.Object, arguments.ValueKind);
        Assert.Equal("Paris", arguments.GetProperty("city").GetString());
    }

    // ── System message across the endpoint switch ───────────────────────────

    /// <summary>
    /// The prompt-completion path prepends <c>config.SystemMessage</c> to the prompt. Without
    /// this, switching to <c>/api/chat</c> for tools would silently drop the system prompt —
    /// exactly when an agent needs it most.
    /// </summary>
    [Fact]
    public async Task ShouldCarryTheConfiguredSystemMessage_OntoTheChatEndpoint()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        var config = WithTools(BaseConfig()) with { SystemMessage = "You are terse." };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(
            [LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken);

        var messages = (await ReadRequestAsync(handler.CapturedRequests.Single())).GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are terse.", messages[0].GetProperty("content").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    /// <summary>A conversation that already carries a system turn must not get a second one.</summary>
    [Fact]
    public async Task ShouldNotDuplicateTheSystemMessage_WhenTheConversationAlreadyHasOne()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        var config = WithTools(BaseConfig()) with { SystemMessage = "From config." };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(
            [LlmMessage.System("From the conversation."), LlmMessage.User("hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        var messages = (await ReadRequestAsync(handler.CapturedRequests.Single())).GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("From the conversation.", messages[0].GetProperty("content").GetString());
    }

    // ── Vision ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ollama does not use content parts: images travel in a separate array of bare base64
    /// strings, with no <c>data:</c> prefix and no media type.
    /// </summary>
    [Fact]
    public async Task ShouldSendImages_AsABareBase64Array()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"a png"},"done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var content = MultiModalContent.Empty()
            .AddText("Describe this")
            .AddImage(ImageContentPart.FromBytes([0x89, 0x50, 0x4E, 0x47], "image/png"));

        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        var request = handler.CapturedRequests.Single();
        Assert.EndsWith("/api/chat", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var message = (await ReadRequestAsync(request)).GetProperty("messages")[0];
        Assert.Equal("Describe this", message.GetProperty("content").GetString());

        var image = message.GetProperty("images")[0].GetString();
        Assert.Equal(Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]), image);
        Assert.DoesNotContain("data:", image!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ollama's server never fetches remote URLs, so an image the caller only referenced
    /// by URL cannot travel in the bare-base64 <c>images</c> array. The converter's
    /// contract says such an image is "reported as skipped rather than silently dropped" —
    /// this pins the report.
    /// </summary>
    [Fact]
    public async Task ShouldWarn_WhenAnImageIsOnlyReferencedByUrl()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var content = MultiModalContent.Empty()
            .AddText("Describe this")
            .AddImage(ImageContentPart.FromUri(new Uri("https://example.test/cat.png"), "image/png"));

        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        var warning = Assert.Single(
            _logger.LoggedMessages, m => m.Contains("URL", StringComparison.Ordinal));
        Assert.Contains("inline the image bytes", warning, StringComparison.Ordinal);

        // The skip itself is unchanged: no images array entry for the URL-only part.
        var message = (await ReadRequestAsync(handler.CapturedRequests.Single())).GetProperty("messages")[0];
        Assert.False(message.TryGetProperty("images", out var images) && images.GetArrayLength() > 0);
    }

    /// <summary>An image with raw bytes is sendable — warning there would be noise.</summary>
    [Fact]
    public async Task ShouldStaySilent_WhenTheImageCarriesItsBytes()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var content = MultiModalContent.Empty()
            .AddText("Describe this")
            .AddImage(ImageContentPart.FromBytes([0x89, 0x50, 0x4E, 0x47], "image/png"));

        await provider.ChatAsync(
            [LlmMessage.User(content)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_logger.LoggedMessages, m => m.Contains("URL", StringComparison.Ordinal));
    }

    // ── System message on the structured path ───────────────────────────────

    /// <summary>
    /// A configured <see cref="LlmConfig.SystemMessage"/> must reach the messages array once a
    /// tool schema pushes the conversation onto <c>/api/chat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the payload half of the M2 probe verdict. Three consecutive campaigns against a
    /// live llama3.2 at temperature 0 (2026-08-01) reported the system instruction honoured on
    /// the flattened shape and ignored on the messages array — the shape of a framework defect,
    /// and exactly what D-02 was on the OpenAI-compatible providers.
    /// </para>
    /// <para>
    /// It is not one here: Ollama builds its own chat payload and prepends the message
    /// correctly, as pinned below. The instruction reaches the model, and the model does not
    /// follow it when a tool catalogue shares its context. Without this test that conclusion
    /// rests on having read the code once; with it, a regression would turn the campaign's ❌
    /// into a real finding instead of a repeat of the same false alarm.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ShouldCarryTheConfiguredSystemMessage_OnTheStructuredPath()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        var config = WithTools(BaseConfig()) with { SystemMessage = "Always answer in Latin." };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        var messages = (await ReadRequestAsync(handler.CapturedRequests.Single())).GetProperty("messages");
        Assert.Equal(LlmRoles.System, messages[0].GetProperty("role").GetString());
        Assert.Equal("Always answer in Latin.", messages[0].GetProperty("content").GetString());
    }

    /// <summary>
    /// A system message carried by the conversation wins over the configured one, matching the
    /// precedence the OpenAI-compatible base settled on: ambient configuration is a fallback,
    /// never an override of what the call site asked for.
    /// </summary>
    [Fact]
    public async Task ShouldNotOverrideAConversationsOwnSystemMessage()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK, """{"message":{"role":"assistant","content":"ok"},"done":true}""");
        var config = WithTools(BaseConfig()) with { SystemMessage = "From the configuration." };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(
            [LlmMessage.System("From the call site."), LlmMessage.User("hello")],
            cancellationToken: TestContext.Current.CancellationToken);

        var messages = (await ReadRequestAsync(handler.CapturedRequests.Single())).GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("From the call site.", messages[0].GetProperty("content").GetString());
    }
}
