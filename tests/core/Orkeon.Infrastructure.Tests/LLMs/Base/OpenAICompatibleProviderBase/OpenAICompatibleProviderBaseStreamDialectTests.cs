using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Base;
using Orkeon.Infrastructure.Tests.Doubles;
using Polly;
using System.Net;
using System.Text;

#pragma warning disable CS0618 // Testing obsolete APIs (LlmConfig.ApiKey)

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// A provider whose dialect names the reasoning trace <c>reasoning</c> (OpenRouter's shape)
/// — the one hook LLM-09 adds to the base, exercised without the rest of that provider.
/// </summary>
public sealed class ReasoningFieldRenamingProvider : OpenAICompatibleProviderBase
{
    public override string Name => "renamed-reasoning";
    protected override Uri DefaultBaseUrl => new("https://api.test.com/v1");
    protected override string DefaultModel => "test-model";
    protected override string ProviderDisplayName => "Renamed";
    protected override string ReasoningFieldName => "reasoning";

    public override LlmProviderCapabilities Capabilities { get; } = new()
    {
        Thinking = ThinkingSupport.EffortOnly,
    };

    public ReasoningFieldRenamingProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<ReasoningFieldRenamingProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }
}

/// <summary>
/// The three base-class changes of LLM-09 (lot A), pinned on replayed streams and bodies:
/// the reasoning field name is a dialect hook (D-05), a chunk carrying a root-level
/// <c>error</c> ends the stream the way a pre-stream refusal does — never as a clean
/// completion (D-07) — and <c>usage.cost</c> becomes the <c>cost</c> metadata (D-08).
/// DeepSeek stands in for every provider that keeps the default field name.
/// </summary>
public sealed class OpenAICompatibleProviderBaseStreamDialectTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "hi" }];

    private const string UpstreamErrorChunk =
        """{"id":"gen-1","error":{"code":502,"message":"Provider returned error"},"choices":[{"index":0,"finish_reason":"error","delta":{}}]}""";

    // ── D-05: the reasoning field is a dialect hook ─────────────────────────

    [Fact]
    public async Task ChatStreaming_reads_the_renamed_reasoning_delta_and_absorbs_comment_lines()
    {
        // OpenRouter's stream: keep-alive comment lines between events, the trace in
        // `delta.reasoning`, never `reasoning_content`.
        var sse = new StringBuilder()
            .AppendLine(": OPENROUTER PROCESSING")
            .AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"reasoning":"weighing"}}]}""")
            .AppendLine()
            .AppendLine(": OPENROUTER PROCESSING")
            .AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"content":"Hello"}}]}""")
            .AppendLine()
            .AppendLine("""data: {"choices":[{"delta":{"content":" world"}}]}""")
            .AppendLine()
            .AppendLine("data: [DONE]")
            .AppendLine()
            .ToString();
        using var provider = CreateRenamedProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var reasoning = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ReasoningDelta);
        Assert.Equal("weighing", reasoning.Delta);
        Assert.Equal(["Hello", " world"], events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).Select(e => e.Delta));
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hello world", completed.Content);
        // The Orkeon-side key keeps its name whatever the vendor calls the field.
        Assert.Equal("weighing", completed.Metadata["reasoning_content"]);
        Assert.False(completed.Metadata.ContainsKey("error"));
    }

    [Fact]
    public async Task ChatStreaming_ignores_a_field_the_dialect_does_not_declare()
    {
        // DeepSeek reads `reasoning_content`; a `reasoning` delta is not its trace.
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"reasoning":"not mine"}}]}""",
            """{"choices":[{"delta":{"reasoning_content":"mine"}}]}""",
            """{"choices":[{"delta":{"content":"answer"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var reasoning = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ReasoningDelta);
        Assert.Equal("mine", reasoning.Delta);
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("mine", completed.Metadata["reasoning_content"]);
    }

    [Fact]
    public async Task Buffered_reads_the_renamed_reasoning_field_under_the_orkeon_key()
    {
        var body = """
            {"id":"gen-1","model":"served/model","choices":[{"message":{"role":"assistant","content":"answer","reasoning":"weighing"}}],
             "usage":{"total_tokens":12,"prompt_tokens":8,"completion_tokens":4}}
            """;
        using var provider = CreateRenamedProvider(body);

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("answer", response.Content);
        Assert.Equal("weighing", response.Metadata["reasoning_content"]);
    }

    [Fact]
    public async Task Buffered_keeps_reading_reasoning_content_on_the_default_dialect()
    {
        var body = """
            {"choices":[{"message":{"role":"assistant","content":"answer","reasoning_content":"mine","reasoning":"not mine"}}],
             "usage":{"total_tokens":12,"prompt_tokens":8,"completion_tokens":4}}
            """;
        using var provider = CreateDeepSeekProvider(body);

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mine", response.Metadata["reasoning_content"]);
    }

    // ── D-07: a mid-stream error is never a clean completion ────────────────

    [Fact]
    public async Task ChatStreaming_root_error_chunk_completes_with_the_error_metadata_and_the_partial_content()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            UpstreamErrorChunk,
            """{"choices":[{"delta":{"content":"lo, never read"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        // What arrived before the error was surfaced live; nothing after it is an answer.
        var delta = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ContentDelta);
        Assert.Equal("Hel", delta.Delta);

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hel", completed.Content);
        // The shape CreateApiErrorResponse gives a 4xx: `error` with the vendor's code and
        // words, `error_type` APIError — so a caller checking `error` sees this one too.
        var error = Assert.IsType<string>(completed.Metadata["error"]);
        Assert.Contains("DeepSeek API error: 502 - Provider returned error", error, StringComparison.Ordinal);
        Assert.Equal("APIError", completed.Metadata["error_type"]);
        Assert.Null(completed.RawResponseBody);
    }

    [Fact]
    public async Task ChatStreaming_root_error_chunk_redacts_secrets_from_the_vendor_message()
    {
        var sse = BuildSseStream(
            """{"error":{"code":401,"message":"key sk-abcdefghijklmnopqrstuvwxyz0123456789 was rejected"}}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        var error = Assert.IsType<string>(completed.Metadata["error"]);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz0123456789", error, StringComparison.Ordinal);
        Assert.Contains("was rejected", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStreaming_null_error_field_is_not_an_error()
    {
        // Some dialects write `"error": null` on a healthy chunk; that is not a failure.
        var sse = BuildSseStream(
            """{"error":null,"choices":[{"delta":{"content":"fine"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("fine", completed.Content);
        Assert.False(completed.Metadata.ContainsKey("error"));
    }

    [Fact]
    public async Task GenerateStreaming_root_error_chunk_throws_like_a_pre_stream_refusal()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hel"}}]}""",
            UpstreamErrorChunk,
            """{"choices":[{"delta":{"content":"lo, never read"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var tokens = new List<string>();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var token in provider.GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
                tokens.Add(token);
        });

        // The token stream has no metadata channel: same exception, same wording as the
        // pre-stream refusal, the vendor's HTTP-shaped code carried as the status.
        Assert.Equal(["Hel"], tokens);
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Contains("DeepSeek API error: 502 - Provider returned error", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateStreaming_root_error_without_an_http_code_carries_no_status()
    {
        var sse = BuildSseStream(
            """{"error":{"message":"upstream exploded","type":"upstream_error"}}""");
        using var provider = CreateDeepSeekProvider(sse);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Null(ex.StatusCode);
        Assert.Contains("DeepSeek API error: upstream_error - upstream exploded", ex.Message, StringComparison.Ordinal);
    }

    // ── D-08: usage.cost becomes the cost metadata ──────────────────────────

    [Fact]
    public async Task ChatStreaming_reads_usage_cost_from_the_final_usage_chunk()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"ok"}}]}""",
            """{"choices":[],"usage":{"total_tokens":42,"prompt_tokens":30,"completion_tokens":12,"cost":0.00042}}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal(42, completed.TokensUsed);
        Assert.Equal(0.00042, Assert.IsType<double>(completed.Metadata["cost"]));
        // A vendor whose provider states no billing currency: the amount travels alone rather
        // than under a unit somebody guessed.
        Assert.False(completed.Metadata.ContainsKey("cost_currency"));
    }

    [Fact]
    public async Task Buffered_reads_usage_cost_and_leaves_the_key_absent_when_the_vendor_writes_none()
    {
        var billed = """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":3,"cost":0.0125}}""";
        using var billedProvider = CreateDeepSeekProvider(billed);
        var billedResponse = await billedProvider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0.0125, Assert.IsType<double>(billedResponse.Metadata["cost"]));
        Assert.False(billedResponse.Metadata.ContainsKey("cost_currency"));

        var unbilled = """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":3}}""";
        using var unbilledProvider = CreateDeepSeekProvider(unbilled);
        var unbilledResponse = await unbilledProvider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(unbilledResponse.Metadata.ContainsKey("cost"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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

    private DeepSeekLlmProvider CreateDeepSeekProvider(string body)
    {
        SetupHttpClient(body);
        var config = LlmConfig.Default() with { MaxRetries = 0, ApiKey = "sk-test", Model = "deepseek-chat" };
        return new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, NullLogger<DeepSeekLlmProvider>.Instance);
    }

    private ReasoningFieldRenamingProvider CreateRenamedProvider(string body)
    {
        SetupHttpClient(body);
        var config = LlmConfig.Default() with { MaxRetries = 0, ApiKey = "sk-test", Model = "test-model" };
        return new ReasoningFieldRenamingProvider(config, _httpClientFactory, _noOpPolicy, NullLogger<ReasoningFieldRenamingProvider>.Instance);
    }

    private void SetupHttpClient(string responseBody)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(responseBody)))
        });
    }

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
