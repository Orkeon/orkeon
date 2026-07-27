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
}
