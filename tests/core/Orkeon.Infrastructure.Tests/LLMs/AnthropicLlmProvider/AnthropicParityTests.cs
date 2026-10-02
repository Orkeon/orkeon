using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Anthropic parity (LLM-05): native SSE streaming (G-20), cache metrics (G-21) and
/// <c>cache_control</c> breakpoints (G-17).
/// </summary>
public class AnthropicParityTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<AnthropicLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly LlmMessage[] Messages = [LlmMessage.User("hello")];

    private static LlmConfig BaseConfig() => LlmConfig.Create("claude-sonnet-5", TestApiKey);

    private AnthropicLlmProvider CreateProvider(LlmConfig config, TestHttpMessageHandler handler)
    {
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        return new AnthropicLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);
    }

    private static async Task<JsonElement> ReadRequestBodyAsync(HttpRequestMessage request)
    {
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // ── G-21: cache metrics ─────────────────────────────────────────────────

    /// <summary>
    /// Anthropic's three input counters do not overlap, so the prompt total is their sum and
    /// the miss side is everything that was not served from the cache. Without these, no
    /// cache-hit ratio is computable on Claude — and no way to tell whether a breakpoint
    /// actually paid off.
    /// </summary>
    [Fact]
    public async Task ShouldSurfaceCacheCounters_AsTypedFields()
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            usage = new
            {
                input_tokens = 10,
                output_tokens = 5,
                cache_creation_input_tokens = 100,
                cache_read_input_tokens = 900,
            },
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        using var provider = CreateProvider(BaseConfig(), handler);

        var response = await provider.ChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(900, response.CacheHitTokens);
        Assert.Equal(110, response.CacheMissTokens);       // fresh input + what was written to cache
        Assert.Equal(1010, response.PromptTokens);          // the three input counters, summed
        Assert.Equal(5, response.CompletionTokens);
        Assert.Equal(1015, response.TokensUsed);
    }

    /// <summary>Absent counters mean "unmeasured", not "zero" — a ratio of 0 would be a lie.</summary>
    [Fact]
    public async Task ShouldLeaveCacheFieldsNull_WhenTheResponseCarriesNoCounters()
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            usage = new { input_tokens = 10, output_tokens = 5 },
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        using var provider = CreateProvider(BaseConfig(), handler);

        var response = await provider.ChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(response.CacheHitTokens);
        Assert.Null(response.CacheMissTokens);
        Assert.Equal(15, response.TokensUsed);
    }

    // ── G-17: cache_control breakpoints ─────────────────────────────────────

    [Fact]
    public async Task ShouldMarkTheSystemPrompt_WhenCachingIsRequested()
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            usage = new { input_tokens = 1, output_tokens = 1 },
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var config = BaseConfig() with
        {
            SystemMessage = "You are a careful assistant.",
            Cache = new LlmCacheConfig { CacheSystemPrompt = true, Ttl = "1h" },
        };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        var payload = await ReadRequestBodyAsync(handler.CapturedRequests.Single());

        // The plain string form cannot carry a breakpoint, so it is promoted to a content block.
        var system = payload.GetProperty("system");
        Assert.Equal(JsonValueKind.Array, system.ValueKind);
        var block = system.EnumerateArray().Single();
        Assert.Equal("You are a careful assistant.", block.GetProperty("text").GetString());
        Assert.Equal("ephemeral", block.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("1h", block.GetProperty("cache_control").GetProperty("ttl").GetString());
    }

    /// <summary>
    /// Caching is opt-in: without an explicit request the payload must be byte-for-byte what
    /// it was before LLM-05, system prompt included.
    /// </summary>
    [Fact]
    public async Task ShouldLeaveTheSystemPromptAsAPlainString_WhenCachingIsNotRequested()
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            usage = new { input_tokens = 1, output_tokens = 1 },
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var config = BaseConfig() with { SystemMessage = "You are a careful assistant." };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        var payload = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.Equal(JsonValueKind.String, payload.GetProperty("system").ValueKind);
    }

    [Fact]
    public async Task ShouldWarn_WhenCachingIsRequestedButThereIsNothingToMark()
    {
        var body = JsonSerializer.Serialize(new
        {
            content = new[] { new { type = "text", text = "ok" } },
            usage = new { input_tokens = 1, output_tokens = 1 },
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        // No system prompt and no tools: the request has no stable prefix to cache.
        var config = BaseConfig() with { Cache = LlmCacheConfig.SystemPrompt() };
        using var provider = CreateProvider(config, handler);

        await provider.ChatAsync(Messages, cancellationToken: TestContext.Current.CancellationToken);

        var payload = await ReadRequestBodyAsync(handler.CapturedRequests.Single());
        Assert.False(payload.TryGetProperty("system", out _));
        Assert.True(_logger.HasLoggedWarning("cache"), "asking for a cache that cannot be placed must be reported");
    }

    // ── G-20: native SSE chat streaming ─────────────────────────────────────

    /// <summary>
    /// Before LLM-05 Anthropic fell back to the buffered emulation in the base class, which
    /// waits for the whole answer before emitting anything — no token ever arrived early.
    /// </summary>
    [Fact]
    public async Task ShouldEmitContentDeltas_ThenACompletedEventCarryingUsage()
    {
        var sse = string.Join("\n\n",
            """data: {"type":"message_start","message":{"usage":{"input_tokens":12,"cache_read_input_tokens":88}}}""",
            """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hel"}}""",
            """data: {"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"pondering"}}""",
            """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"lo"}}""",
            """data: {"type":"message_delta","usage":{"output_tokens":7}}""",
            """data: {"type":"message_stop"}""",
            "") + "\n";

        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(
            Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(ev);
        }

        var contentDeltas = events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).ToList();
        Assert.Equal(["Hel", "lo"], contentDeltas.Select(e => e.Delta));

        var reasoning = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ReasoningDelta);
        Assert.Equal("pondering", reasoning.Delta);

        var completed = events[^1];
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
        Assert.Equal("Hello", completed.FinalResponse!.Content);
        Assert.Equal(88, completed.FinalResponse.CacheHitTokens);
        Assert.Equal(7, completed.FinalResponse.CompletionTokens);
        Assert.Equal(100, completed.FinalResponse.PromptTokens);
        Assert.Equal("pondering", completed.FinalResponse.Metadata["reasoning_content"]);
    }

    /// <summary>
    /// GAP-32: a streamed turn calls its tools. The Messages API streams a tool call as a
    /// <c>tool_use</c> block — its id and name first, its input in <c>input_json_delta</c>
    /// fragments —, which the stream used to drop: the final response carried the text alone, and
    /// an agent streamed on Claude never called a tool. The final response now carries them in the
    /// body the buffered path returns, which the chat client reads.
    /// </summary>
    [Fact]
    public async Task ShouldAssembleStreamedToolUseBlocks_IntoTheFinalResponse()
    {
        var sse = string.Join("\n\n",
            """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""",
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Looking it up."}}""",
            """data: {"type":"content_block_stop","index":0}""",
            """data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"web_search","input":{}}}""",
            """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"query\": \"orke"}}""",
            """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"on\"}"}}""",
            """data: {"type":"content_block_stop","index":1}""",
            """data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":9}}""",
            """data: {"type":"message_stop"}""",
            "") + "\n";
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        using var provider = new AnthropicLlmProvider(
            BaseConfig(), _httpClientFactory, new Orkeon.Infrastructure.LLMs.ToolCalling.AnthropicToolCallingStrategy(), _logger);
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(provider);

        var response = await Microsoft.Extensions.AI.ChatResponseExtensions.ToChatResponseAsync(
            chatClient.GetStreamingResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "search")],
                cancellationToken: TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        Assert.Equal("Looking it up.", response.Text);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionCallContent>());
        Assert.Equal("toolu_01", call.CallId);
        Assert.Equal("web_search", call.Name);
        Assert.Equal("orkeon", call.Arguments!["query"]?.ToString());
        Assert.Equal(21, response.Usage?.TotalTokenCount);
    }

    /// <summary>
    /// GAP-32 — on the native protocol, a model that writes its call in the text protocol: the
    /// streamed answer carries a Messages API body, as the buffered one does, so the fallback parser
    /// reads the call there too and a streamed agent turn calls the same tool.
    /// </summary>
    [Fact]
    public async Task ShouldCarryABody_WhenAStreamedAnswerWritesItsCallAsText()
    {
        var sse = string.Join("\n\n",
            """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""",
            """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Looking. [TOOL_CALL]{tool => \"web_search\", "}}""",
            """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"args => {--query \"orkeon\"}}[/TOOL_CALL]"}}""",
            """data: {"type":"content_block_stop","index":0}""",
            """data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":9}}""",
            """data: {"type":"message_stop"}""",
            "") + "\n";
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        using var provider = new AnthropicLlmProvider(
            BaseConfig(), _httpClientFactory, new Orkeon.Infrastructure.LLMs.ToolCalling.AnthropicToolCallingStrategy(), _logger);
        using var chatClient = new Orkeon.Infrastructure.LLMs.Adapters.LlmProviderToChatClientAdapter(
            provider,
            textFallbackParser: new Orkeon.Infrastructure.LLMs.ToolCalling.TextFallbackToolCallParser(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Orkeon.Infrastructure.LLMs.ToolCalling.TextFallbackToolCallParser>.Instance));

        var response = await Microsoft.Extensions.AI.ChatResponseExtensions.ToChatResponseAsync(
            chatClient.GetStreamingResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "search")],
                cancellationToken: TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<Microsoft.Extensions.AI.FunctionCallContent>());
        Assert.Equal("web_search", call.Name);
        Assert.Equal("orkeon", call.Arguments!["query"]?.ToString());
    }

    [Fact]
    public async Task ShouldCompleteWithAnError_WhenTheStreamingCallFails()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}""");
        using var provider = CreateProvider(BaseConfig(), handler);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(
            Messages, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(ev);
        }

        var completed = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
        Assert.Contains("TooManyRequests", completed.FinalResponse!.Metadata["error"]!.ToString(), StringComparison.Ordinal);
    }
}
