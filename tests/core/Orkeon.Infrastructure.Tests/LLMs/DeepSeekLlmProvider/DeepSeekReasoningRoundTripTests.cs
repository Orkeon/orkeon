using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Regression coverage for the round-36 incident: DeepSeek thinking-mode models
/// (deepseek-v4-flash, deepseek-reasoner) reject every multi-turn continuation with
/// HTTP 400 — <c>"The reasoning_content in the thinking mode must be passed back to
/// the API."</c> — when the previous assistant message's <c>reasoning_content</c>
/// is dropped from the conversation history. These tests pin the round-trip:
///
/// 1. The provider lifts <c>reasoning_content</c> off the response into <see cref="LlmResponse.Metadata"/>.
/// 2. The adapter parks it on <see cref="ChatMessage.AdditionalProperties"/> so M.E.AI
///    keeps it across turns.
/// 3. On the next request, the adapter rehydrates it onto the outgoing
///    <see cref="LlmMessage.ReasoningContent"/>.
/// 4. <see cref="Infrastructure.LLMs.DeepSeekLlmProvider"/> serialises the field back into the
///    OpenAI-style assistant message dictionary.
/// </summary>
public class DeepSeekReasoningRoundTripTests
{
    private const string ReasoningKey = "reasoning_content";
    private const string ReasoningTrace = "Step 1: locate /src. Step 2: enumerate packages.";

    [Fact]
    public async Task ShouldEmitReasoningContent_WhenAssistantMessageCarriesIt()
    {
        // Arrange: capture the wire payload so we can assert reasoning_content survived.
        using var handler = new CapturingHandler(MinimalToolCallResponse);
        var factory = SingleClientFactory.From(handler, "https://api.deepseek.com");

        var strategy = new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);
        var config = LlmConfig.Create("deepseek-reasoner", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com"),
            Tools = new List<ToolSchema> { SampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };

        using var provider = new Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider(
            config, factory, strategy,
            new TestDoubles.TestLogger<Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider>());

        var messages = new[]
        {
            LlmMessage.User("Map /src"),
            new LlmMessage
            {
                Role = "assistant",
                Content = string.Empty,
                RawToolCalls = """[{"id":"call_1","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]""",
                ReasoningContent = ReasoningTrace
            },
            new LlmMessage { Role = "tool", Content = "ok", ToolCallId = "call_1" }
        };

        // Act
        await provider.ChatAsync(messages, config, TestContext.Current.CancellationToken);

        // Assert: reasoning_content travelled on the wire on the assistant turn.
        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var assistant = FindFirstMessage(doc.RootElement, role: "assistant");
        Assert.True(assistant.TryGetProperty(ReasoningKey, out var rc),
            "DeepSeekLlmProvider must emit reasoning_content on the assistant message in the request payload.");
        Assert.Equal(ReasoningTrace, rc.GetString());
    }

    [Fact]
    public async Task ShouldOmitReasoningContent_WhenAssistantMessageHasNone()
    {
        // Arrange
        using var handler = new CapturingHandler(MinimalToolCallResponse);
        var factory = SingleClientFactory.From(handler, "https://api.deepseek.com");

        var strategy = new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);
        var config = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com"),
            Tools = new List<ToolSchema> { SampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };

        using var provider = new Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider(
            config, factory, strategy,
            new TestDoubles.TestLogger<Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider>());

        var messages = new[]
        {
            LlmMessage.User("Map /src"),
            new LlmMessage
            {
                Role = "assistant",
                Content = "let me try",
                RawToolCalls = """[{"id":"call_2","type":"function","function":{"name":"directory_read","arguments":"{}"}}]"""
            },
            new LlmMessage { Role = "tool", Content = "ok", ToolCallId = "call_2" }
        };

        // Act
        await provider.ChatAsync(messages, config, TestContext.Current.CancellationToken);

        // Assert: no spurious reasoning_content key when the source LlmMessage had none.
        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var assistant = FindFirstMessage(doc.RootElement, role: "assistant");
        Assert.False(assistant.TryGetProperty(ReasoningKey, out _),
            "DeepSeekLlmProvider must not invent reasoning_content when the source message lacks it.");
    }

    [Fact]
    public async Task ShouldEmitReasoningContent_OnPlainAssistantTurnWithoutToolCalls()
    {
        // Guards the second branch of FormatChatMessage: assistant messages without tool_calls.
        using var handler = new CapturingHandler(MinimalTextResponse);
        var factory = SingleClientFactory.From(handler, "https://api.deepseek.com");

        var strategy = new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);
        var config = LlmConfig.Create("deepseek-reasoner", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com"),
            Tools = new List<ToolSchema> { SampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };

        using var provider = new Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider(
            config, factory, strategy,
            new TestDoubles.TestLogger<Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider>());

        var messages = new[]
        {
            LlmMessage.User("Hello"),
            new LlmMessage
            {
                Role = "assistant",
                Content = "Hi",
                ReasoningContent = ReasoningTrace
            },
            LlmMessage.User("Continue")
        };

        await provider.ChatAsync(messages, config, TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var assistant = FindFirstMessage(doc.RootElement, role: "assistant");
        Assert.True(assistant.TryGetProperty(ReasoningKey, out var rc));
        Assert.Equal(ReasoningTrace, rc.GetString());
    }

    [Fact]
    public async Task ShouldRoundTripReasoningContent_ThroughChatClientAdapter()
    {
        // Arrange: a single mocked DeepSeek response with reasoning_content + tool_calls.
        // Round-trip: provider response → adapter ChatResponse → AdditionalProperties → next LlmMessage.
        using var handler = new CapturingHandler(ReasoningWithToolCallResponse);
        var factory = SingleClientFactory.From(handler, "https://api.deepseek.com");

        var strategy = new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance);
        var baseConfig = LlmConfig.Create("deepseek-reasoner", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com"),
            Tools = new List<ToolSchema> { SampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };

        using var provider = new Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider(
            baseConfig, factory, strategy,
            new TestDoubles.TestLogger<Orkeon.Infrastructure.LLMs.DeepSeekLlmProvider>());

        using var adapter = new LlmProviderToChatClientAdapter(provider, baseConfig);

        var initial = new[] { new ChatMessage(ChatRole.User, "Map /src") };

        // Act 1: get the response — should expose reasoning_content via AdditionalProperties.
        var first = await adapter.GetResponseAsync(initial, cancellationToken: TestContext.Current.CancellationToken);

        // Assert 1: the assistant message in the response carries reasoning_content.
        var assistantMsg = first.Messages.First();
        Assert.NotNull(assistantMsg.AdditionalProperties);
        Assert.True(assistantMsg.AdditionalProperties!.TryGetValue(ReasoningKey, out var roundTripped));
        Assert.Equal(ReasoningTrace, roundTripped as string);

        // Act 2: round-trip the message back into the next provider call.
        // Reproduce the realistic conversation shape: [user, assistant_with_tool_call, tool_result].
        var followUp = initial
            .Concat(first.Messages)
            .Append(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call_99", "directory contents")]))
            .ToList();

        await adapter.GetResponseAsync(followUp, cancellationToken: TestContext.Current.CancellationToken);

        // Assert 2: the second outgoing payload carries reasoning_content on the assistant turn.
        Assert.NotNull(handler.LastBody);
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var assistantOnWire = FindFirstMessage(doc.RootElement, role: "assistant");
        Assert.True(assistantOnWire.TryGetProperty(ReasoningKey, out var rc),
            "Adapter must replay reasoning_content from AdditionalProperties on the next request.");
        Assert.Equal(ReasoningTrace, rc.GetString());
    }

    // ─────────────────────────────────────────────────────────────
    //  Test helpers
    // ─────────────────────────────────────────────────────────────

    private static JsonElement FindFirstMessage(JsonElement root, string role)
    {
        foreach (var m in root.GetProperty("messages").EnumerateArray())
        {
            if (m.GetProperty("role").GetString() == role)
                return m;
        }
        throw new Xunit.Sdk.XunitException($"No message with role={role} in payload.");
    }

    private static readonly ToolSchema SampleTool = new(
        Name: "directory_read",
        Description: "Lists files in a directory",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["path"] = new ParameterSchema("string", "The directory path", Required: true)
        });

    private const string MinimalToolCallResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_99","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]}}],"usage":{"total_tokens":42}}
        """;

    private const string MinimalTextResponse = """
        {"choices":[{"message":{"role":"assistant","content":"OK"}}],"usage":{"total_tokens":7}}
        """;

    // Same as MinimalToolCallResponse but with reasoning_content, so the adapter has something to round-trip.
    private static readonly string ReasoningWithToolCallResponse = """
        {"choices":[{"message":{"role":"assistant","content":null,"reasoning_content":"REASONING_PLACEHOLDER","tool_calls":[{"id":"call_99","type":"function","function":{"name":"directory_read","arguments":"{\"path\":\"/src\"}"}}]}}],"usage":{"total_tokens":42}}
        """.Replace("REASONING_PLACEHOLDER", ReasoningTrace);

    /// <summary>
    /// Captures the most recent request body so tests can assert on the wire payload.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string? LastBody { get; private set; }

        public CapturingHandler(string responseJson) { _response = responseJson; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        private SingleClientFactory(HttpClient client) { _client = client; }

        public HttpClient CreateClient(string name) => _client;

        public static SingleClientFactory From(HttpMessageHandler handler, string baseAddress)
            => new(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) });
    }
}
