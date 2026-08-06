using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Polly;
using System.Net;
using System.Text;
using System.Text.Json;

#pragma warning disable CS0618 // Testing obsolete APIs (LlmConfig.ApiKey)

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// exp07 F5 L2: <c>ChatStreamingAsync</c> on the OpenAI-compatible SSE path — content and
/// DeepSeek <c>reasoning_content</c> deltas, final usage chunk (<c>include_usage</c>),
/// multi-chunk tool-call accumulation with a synthesized <c>RawResponseBody</c>, and the
/// buffered fallback of <c>HttpLlmProviderBase</c>.
/// </summary>
public sealed class ChatStreamingTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy =
        Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "hi" }];
    private static readonly string[] HelloWorldDeltas = ["Hello", " world"];

    [Fact]
    public async Task ChatStreaming_yields_content_deltas_then_completed_with_assembled_content()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"delta":{"content":" world"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var deltas = events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).Select(e => e.Delta).ToList();
        Assert.Equal(HelloWorldDeltas, deltas);
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal("Hello world", completed.FinalResponse!.Content);
        Assert.Null(completed.FinalResponse.RawResponseBody); // no tool calls streamed
    }

    [Fact]
    public async Task ChatStreaming_surfaces_reasoning_deltas_and_final_reasoning_metadata()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"reasoning_content":"thinking..."}}]}""",
            """{"choices":[{"delta":{"content":"answer"}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var reasoning = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ReasoningDelta);
        Assert.Equal("thinking...", reasoning.Delta);
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal("answer", completed.FinalResponse!.Content);
        Assert.Equal("thinking...", completed.FinalResponse.Metadata["reasoning_content"]);
    }

    [Fact]
    public async Task ChatStreaming_captures_usage_from_the_final_include_usage_chunk()
    {
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"content":"ok"}}]}""",
            """{"choices":[],"usage":{"total_tokens":42,"prompt_tokens":30,"completion_tokens":12,"prompt_cache_hit_tokens":25,"prompt_cache_miss_tokens":5}}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var final = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal(42, final.TokensUsed);
        Assert.Equal(30, final.PromptTokens);
        Assert.Equal(12, final.CompletionTokens);
        Assert.Equal(25, final.CacheHitTokens);
        Assert.Equal(5, final.CacheMissTokens);
    }

    [Fact]
    public async Task ChatStreaming_accumulates_multi_chunk_tool_calls_into_a_parsable_body()
    {
        // The arguments string arrives split across three fragments (OpenAI stream shape).
        var sse = BuildSseStream(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"file_read","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"/x\"}"}}]}}]}""");
        using var provider = CreateDeepSeekProvider(sse);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var final = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.NotNull(final.RawResponseBody);

        // The synthesized body must be consumable by the standard OpenAI tool-call shape.
        using var doc = JsonDocument.Parse(final.RawResponseBody!);
        var fn = doc.RootElement.GetProperty("choices")[0].GetProperty("message")
            .GetProperty("tool_calls")[0].GetProperty("function");
        Assert.Equal("file_read", fn.GetProperty("name").GetString());
        Assert.Equal("""{"path":"/x"}""", fn.GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task ChatStreaming_http_error_completes_with_an_error_response()
    {
        SetupHttpClient("boom", HttpStatusCode.InternalServerError);
        var config = LlmConfig.Default() with { MaxRetries = 0, ApiKey = "sk-test", Model = "deepseek-chat" };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<DeepSeekLlmProvider>.Instance);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal("", completed.FinalResponse!.Content);
        Assert.True(completed.FinalResponse.Metadata.ContainsKey("error"));
    }

    [Fact]
    public async Task ChatStreaming_missing_api_key_falls_back_to_buffered_single_completed()
    {
        SetupHttpClient("", HttpStatusCode.OK);
        var config = LlmConfig.Default() with { MaxRetries = 0, ApiKey = "", Model = "deepseek-chat" };
        using var provider = new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<DeepSeekLlmProvider>.Instance);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        // Buffered fallback: no content delta (missing-key error response has empty content),
        // exactly one Completed event.
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed);
        Assert.Equal("", completed.FinalResponse!.Content);
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

    private DeepSeekLlmProvider CreateDeepSeekProvider(string sseBody)
    {
        SetupHttpClient(sseBody, HttpStatusCode.OK);
        var config = LlmConfig.Default() with { MaxRetries = 0, ApiKey = "sk-test", Model = "deepseek-chat" };
        return new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy,
            NullLogger<DeepSeekLlmProvider>.Instance);
    }

    private void SetupHttpClient(string responseBody, HttpStatusCode statusCode)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        // Factory, not a fixed instance: the streaming connect retry consumes (and disposes)
        // one response per attempt, exactly like a real HttpClient produces a fresh response
        // per SendAsync.
        handler.SetResponseFactory(_ => new HttpResponseMessage(statusCode)
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
