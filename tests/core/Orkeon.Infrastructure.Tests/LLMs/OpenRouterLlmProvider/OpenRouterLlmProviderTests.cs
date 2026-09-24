using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// OpenRouter provider: OpenAI-compatible transport against <c>openrouter.ai/api/v1</c>,
/// with the four things the marketplace does differently (LLM-09): the <c>reasoning</c>
/// field, the <c>reasoning</c> request object, <c>usage.cost</c> and the attribution
/// headers. Documentation-backed on 2026-09-18 — every pin below restates the vendor's
/// documentation, not a live measurement (the first campaign is LLM-09 §6).
/// </summary>
public sealed class OpenRouterLlmProviderTests : IDisposable
{
    private const string SampleSchema =
        """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"]}""";

    private static readonly LlmMessage[] OneUserMessage = [LlmMessage.User("hello")];

    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<OpenRouterLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    private readonly LlmConfig _config = LlmConfig.Create("google/gemini-3.7-flash", TestApiKey) with { MaxRetries = 0 };
    private TestHttpMessageHandler? _handler;

    private static string OkResponse(string content, int tokens) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
        usage = new { total_tokens = tokens }
    });

    private OpenRouterLlmProvider CreateProvider(HttpStatusCode status, string body, LlmConfig? config = null)
    {
        _handler = TestHttpMessageHandler.CreateWithResponse(status, body);
        _httpClientFactory.RegisterClient(nameof(OpenRouterLlmProvider), new HttpClient(_handler));
        return new OpenRouterLlmProvider(config ?? _config, _httpClientFactory, _noOpPolicy, _logger);
    }

    private OpenRouterLlmProvider CreateStreamingProvider(string sse)
    {
        _handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(sse))),
        });
        _httpClientFactory.RegisterClient(nameof(OpenRouterLlmProvider), new HttpClient(_handler));
        return new OpenRouterLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
    }

    private async Task<JsonElement> SentPayloadAsync()
    {
        var request = Assert.Single(_handler!.CapturedRequests);
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // ── Identity and declaration ────────────────────────────────────────────

    [Fact]
    public void ShouldReturnOpenRouter_WhenName()
    {
        using var provider = new OpenRouterLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("openrouter", provider.Name);
    }

    [Fact]
    public void ShouldDeclareDocumentedCapabilities()
    {
        using var provider = new OpenRouterLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Documented 2026-09-18 (D-04): json_schema per endpoint, the reasoning object with
        // enabled / effort / max_tokens (Budget — a transport declaration, per model in
        // reality), vision per model. Nothing replayed, no explicit cache breakpoint.
        Assert.Equal(ResponseFormatSupport.JsonSchema, provider.Capabilities.ResponseFormat);
        Assert.Equal(ThinkingSupport.Budget, provider.Capabilities.Thinking);
        Assert.True(provider.Capabilities.Vision);
        Assert.False(provider.Capabilities.ReplaysReasoningContent);
        Assert.False(provider.Capabilities.ExplicitPromptCaching);
        Assert.False(provider.Capabilities.RequiresJsonKeywordInPrompt);
    }

    // ── Transport ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldCallTheMarketplaceEndpoint_WithBearerAndBothAttributionHeaders_WhenGenerateAsync()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Content);
        var request = Assert.Single(_handler!.CapturedRequests);
        Assert.Equal("openrouter.ai", request.RequestUri!.Host);
        Assert.Equal("/api/v1/chat/completions", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(TestApiKey, request.Headers.Authorization?.Parameter);

        // D-06: the vendor documents `HTTP-Referer` literally — not the standard `Referer`
        // the framework's Referrer property would write — and `X-OpenRouter-Title`.
        Assert.Equal("https://github.com/Orkeon/orkeon", Assert.Single(request.Headers.GetValues("HTTP-Referer")));
        Assert.Equal("Orkeon", Assert.Single(request.Headers.GetValues("X-OpenRouter-Title")));
        Assert.False(request.Headers.Contains("Referer"));
    }

    [Fact]
    public async Task ShouldSendTheAttributionHeaders_OnTheChatPathToo()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(_handler!.CapturedRequests);
        Assert.True(request.Headers.Contains("HTTP-Referer"));
        Assert.True(request.Headers.Contains("X-OpenRouter-Title"));
    }

    // ── Payload: the reasoning object and the schema ────────────────────────

    [Fact]
    public async Task ShouldWriteTheReasoningObject_AndNeitherThinkingNorTopLevelEffort()
    {
        var config = _config with
        {
            Thinking = new LlmThinkingConfig { Enabled = true, Effort = "low", BudgetTokens = 2048 },
        };
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3), config);

        await provider.ChatAsync(OneUserMessage, config, TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        var reasoning = payload.GetProperty("reasoning");
        Assert.True(reasoning.GetProperty("enabled").GetBoolean());
        Assert.Equal("low", reasoning.GetProperty("effort").GetString());
        Assert.Equal(2048, reasoning.GetProperty("max_tokens").GetInt32());
        Assert.Equal(3, reasoning.EnumerateObject().Count());
        // The DeepSeek/GLM block and the first-level effort would say the same thing twice.
        Assert.False(payload.TryGetProperty("thinking", out _));
        Assert.False(payload.TryGetProperty("reasoning_effort", out _));
        // A declared Budget: no warning about the token budget.
        Assert.DoesNotContain(_logger.LoggedMessages, m => m.Contains("budgetTokens", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShouldWriteTheReasoningObject_OnTheSinglePromptPathToo()
    {
        var config = _config with { Thinking = new LlmThinkingConfig { Effort = "high" } };
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3), config);

        await provider.GenerateAsync("hello", config, TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.Equal("high", payload.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(payload.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task ShouldWriteNoReasoningObject_WhenNoThinkingOptionIsDeclared()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.False(payload.TryGetProperty("reasoning", out _));
        Assert.False(payload.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task ShouldSendTheSchema_AsResponseFormatJsonSchema()
    {
        var config = _config with { ResponseFormat = LlmResponseFormat.JsonSchema("answer_shape", SampleSchema) };
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3), config);

        await provider.ChatAsync(OneUserMessage, config, TestContext.Current.CancellationToken);

        var format = (await SentPayloadAsync()).GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("answer_shape", format.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Object, format.GetProperty("json_schema").GetProperty("schema").ValueKind);
    }

    // ── Buffered response: the reasoning field, the cost and its breakdown ──

    [Fact]
    public async Task ShouldReadMessageReasoning_AndTheUsageBreakdown_WhenBuffered()
    {
        const string body = """
            {"id":"gen-1","model":"google/gemini-3.7-flash","choices":[{"message":{"role":"assistant","content":"answer","reasoning":"weighing"}}],
             "usage":{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120,"cost":0.00042,
                      "cost_details":{"upstream_inference_cost":0.0004},"is_byok":false,
                      "prompt_tokens_details":{"cached_tokens":60,"cache_write_tokens":40},
                      "completion_tokens_details":{"reasoning_tokens":12}}}
            """;
        using var provider = CreateProvider(HttpStatusCode.OK, body);

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("answer", response.Content);
        Assert.Equal("weighing", response.Metadata["reasoning_content"]);
        Assert.Equal(0.00042, Assert.IsType<double>(response.Metadata["cost"]));
        // OpenRouter bills in credits, and a credit is a dollar: the provider says so, so no
        // reader downstream has to guess the unit (STUDIO-29).
        Assert.Equal("USD", response.Metadata["cost_currency"]);
        Assert.Equal(0.0004, Assert.IsType<double>(response.Metadata["upstream_inference_cost"]));
        Assert.False(Assert.IsType<bool>(response.Metadata["is_byok"]));
        Assert.Equal(40, response.Metadata["cache_write_tokens"]);
        Assert.Equal(12, response.Metadata["reasoning_tokens"]);
        Assert.Equal("google/gemini-3.7-flash", response.Metadata["served_model"]);
        // The read side of the cache lands in the typed fields, generically.
        Assert.Equal(60, response.CacheHitTokens);
        Assert.Equal(40, response.CacheMissTokens);
        Assert.Equal(100, response.Metadata["prompt_tokens"]);
    }

    [Fact]
    public async Task ShouldLeaveTheOptionalMetadataAbsent_WhenTheResponseCarriesNone()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        foreach (var key in new[] { "reasoning_content", "cost", "cost_currency", "upstream_inference_cost", "is_byok", "cache_write_tokens", "reasoning_tokens", "served_model" })
            Assert.False(response.Metadata.ContainsKey(key), $"unexpected metadata '{key}'");
    }

    // ── Streams ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldStreamReasoningDeltas_AndKeepContentIntact_AcrossProcessingComments()
    {
        var sse = new StringBuilder()
            .AppendLine(": OPENROUTER PROCESSING").AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"reasoning":"weighing"}}]}""").AppendLine()
            .AppendLine(": OPENROUTER PROCESSING").AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"content":"Hello"}}]}""").AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"content":" world"}}]}""").AppendLine()
            .AppendLine("""data: {"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":2,"total_tokens":12,"cost":0.00001}}""").AppendLine()
            .AppendLine("data: [DONE]").AppendLine()
            .ToString();
        using var provider = CreateStreamingProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("weighing", Assert.Single(events, e => e.Kind == LlmStreamEventKind.ReasoningDelta).Delta);
        Assert.Equal(["Hello", " world"], events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).Select(e => e.Delta));
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hello world", completed.Content);
        Assert.Equal("weighing", completed.Metadata["reasoning_content"]);
        Assert.Equal(12, completed.TokensUsed);
        // usage.cost on the streamed path: the final usage chunk arrives unasked.
        Assert.Equal(0.00001, Assert.IsType<double>(completed.Metadata["cost"]));
        Assert.Equal("USD", completed.Metadata["cost_currency"]);
        Assert.False(completed.Metadata.ContainsKey("error"));
    }

    [Fact]
    public async Task ShouldCompleteWithTheErrorMetadata_NeverCleanly_WhenTheChatStreamFailsMidway()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            """{"id":"gen-1","error":{"code":502,"message":"Provider returned error","metadata":{"provider_name":"Google"}},"choices":[{"index":0,"finish_reason":"error","delta":{}}]}""");
        using var provider = CreateStreamingProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hel", completed.Content);
        var error = Assert.IsType<string>(completed.Metadata["error"]);
        Assert.Contains("OpenRouter API error: 502 - Provider returned error", error, StringComparison.Ordinal);
        Assert.Equal("APIError", completed.Metadata["error_type"]);
    }

    [Fact]
    public async Task ShouldThrow_WhenTheTokenStreamFailsMidway()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            """{"error":{"code":503,"message":"No provider satisfies the routing"},"choices":[{"finish_reason":"error","delta":{}}]}""");
        using var provider = CreateStreamingProvider(sse);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Contains("OpenRouter API error: 503 - No provider satisfies the routing", ex.Message, StringComparison.Ordinal);
    }

    // ── Errors ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldSurfaceTheVendorMessage_WhenCreditsAreExhausted()
    {
        using var provider = CreateProvider(
            HttpStatusCode.PaymentRequired,
            """{"error":{"message":"Insufficient credits. Add more using https://openrouter.ai/settings/credits","code":402}}""");

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("", response.Content);
        var error = Assert.IsType<string>(response.Metadata["error"]);
        Assert.Contains("PaymentRequired", error, StringComparison.Ordinal);
        Assert.Contains("Insufficient credits", error, StringComparison.Ordinal);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static async Task<List<LlmStreamEvent>> Collect(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in stream.WithCancellation(TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }

    private static string BuildSseStream(params string[] jsonPayloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in jsonPayloads)
        {
            sb.AppendLine($"data: {payload}");
            sb.AppendLine();
        }
        sb.AppendLine("data: [DONE]");
        sb.AppendLine();
        return sb.ToString();
    }

    public void Dispose() => _handler?.Dispose();
}
