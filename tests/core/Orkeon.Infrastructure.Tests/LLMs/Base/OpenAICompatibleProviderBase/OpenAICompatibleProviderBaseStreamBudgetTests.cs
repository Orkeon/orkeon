using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Polly;
using System.Net;
using System.Text;

#pragma warning disable CS0618 // Testing obsolete APIs (LlmConfig.ApiKey)

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// LLM-12: a streamed call runs under the same <c>Llm:TimeoutSeconds</c> as a buffered one
/// (headers and body) and, when set, under <c>Llm:StreamIdleSeconds</c> between two lines; a
/// stream the vendor closed before any answer is a failed call. Before, <c>HttpClient.Timeout</c>
/// stopped counting at the headers, and a GLM model that thought for minutes — or a gateway that
/// cut the stream — came back as an empty answer that the agent loop blamed on <c>Llm:MaxTokens</c>
/// and retried without tools. The budgets here are one second: nothing asserts a duration, the
/// test's own token is the hang guard.
/// </summary>
public sealed class OpenAICompatibleProviderBaseStreamBudgetTests : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "hi" }];

    private const string HelloDelta = """data: {"choices":[{"delta":{"content":"Hel"}}]}""";
    private const string ReasoningDelta = """data: {"choices":[{"delta":{"reasoning_content":"weighing the options"}}]}""";

    // ── Llm:TimeoutSeconds bounds the body ────────────────────────────────────

    [Fact]
    public async Task The_total_bound_ends_a_stream_that_hangs_after_its_first_delta_with_the_timeout_sentence()
    {
        var handler = ServeThenHang(HelloDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 1, idleSeconds: null);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var delta = Assert.Single(events, e => e.Kind == LlmStreamEventKind.ContentDelta);
        Assert.Equal("Hel", delta.Delta);
        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hel", completed.Content);
        Assert.Contains("Llm:TimeoutSeconds = 1 s", completed.Error, StringComparison.Ordinal);
        Assert.Contains("failed call, not an empty answer", completed.Error, StringComparison.Ordinal);
        Assert.Equal(nameof(TaskCanceledException), completed.ErrorType);
        Assert.Null(completed.RawResponseBody);
        Assert.Equal(1, handler.SendCallCount);
    }

    [Fact]
    public async Task The_idle_bound_ends_a_stream_that_goes_silent_with_its_own_sentence()
    {
        ServeThenHang(HelloDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: 1);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hel", completed.Content);
        Assert.Contains("Llm:StreamIdleSeconds = 1 s", completed.Error, StringComparison.Ordinal);
        Assert.Equal("StreamIdleTimeout", completed.ErrorType);
        Assert.Null(completed.RawResponseBody);
    }

    [Fact]
    public async Task The_total_bound_ends_a_call_whose_headers_never_arrive_without_spending_the_retry_budget()
    {
        // A handler that never answers but honours the token — what a stalled endpoint looks like.
        using var hanging = new HangingHandler();
        using var client = new HttpClient(hanging, disposeHandler: false);
        _httpClientFactory.SetDefaultClient(client);
        using var provider = Provider(timeoutSeconds: 1, idleSeconds: null, maxRetries: 10);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var only = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, only.Kind);
        var completed = only.FinalResponse!;
        Assert.Equal(string.Empty, completed.Content);
        Assert.Contains("Llm:TimeoutSeconds = 1 s", completed.Error, StringComparison.Ordinal);
        Assert.InRange(hanging.Calls, 1, 2);   // the budget, not Llm:MaxRetries = 10
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_propagates_during_the_hang()
    {
        ServeThenHang(HelloDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: null);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var events = new List<LlmStreamEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var ev in provider.ChatStreamingAsync(OneUserMessage, cancellationToken: caller.Token))
            {
                events.Add(ev);
                if (ev.Kind == LlmStreamEventKind.ContentDelta)
                    await caller.CancelAsync();
            }
        });

        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.Completed);
    }

    // ── A stream closed before its finish marker ──────────────────────────────

    [Fact]
    public async Task A_stream_closed_before_any_answer_is_a_failed_call_that_names_the_reasoning_it_streamed()
    {
        // GLM thought, then the gateway closed the connection: no content, no finish_reason, no [DONE].
        Serve(ReasoningDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: null);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal(string.Empty, completed.Content);
        Assert.Equal("StreamTruncated", completed.ErrorType);
        Assert.Contains("closed the stream before any answer arrived", completed.Error, StringComparison.Ordinal);
        Assert.Contains("characters of reasoning were streamed", completed.Error, StringComparison.Ordinal);
        Assert.Null(completed.RawResponseBody);
    }

    [Fact]
    public async Task A_stream_closed_after_some_answer_is_served_and_flagged_as_truncated()
    {
        Serve(HelloDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: null);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("Hel", completed.Content);
        Assert.Null(completed.Error);
        Assert.Equal(true, completed.Metadata[LlmResponseMetadataKeys.StreamTruncated]);
    }

    [Theory]
    [InlineData("""data: {"choices":[{"delta":{"content":"Hi"},"finish_reason":"stop"}]}""" + "\n\n")]
    [InlineData(HelloDelta + "\n\ndata: [DONE]\n\n")]
    public async Task A_stream_that_ends_on_either_finish_marker_is_a_clean_answer(string body)
    {
        Serve(body);
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: 1);

        var events = await Collect(provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken));

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Null(completed.Error);
        Assert.False(completed.Metadata.ContainsKey(LlmResponseMetadataKeys.StreamTruncated));
    }

    // ── The text stream has no metadata channel: it throws ────────────────────

    [Fact]
    public async Task The_text_stream_fails_with_the_timeout_sentence_when_the_body_hangs()
    {
        ServeThenHang(HelloDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 1, idleSeconds: null);
        var tokens = new List<string>();

        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var token in provider.GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
                tokens.Add(token);
        });

        Assert.Equal(["Hel"], tokens);
        Assert.Contains("Llm:TimeoutSeconds = 1 s", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_text_stream_fails_when_the_vendor_closed_it_before_any_token()
    {
        Serve(ReasoningDelta + "\n\n");
        using var provider = Provider(timeoutSeconds: 600, idleSeconds: null);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Contains("closed the stream before any answer arrived", failure.Message, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static async Task<List<LlmStreamEvent>> Collect(IAsyncEnumerable<LlmStreamEvent> stream)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in stream.WithCancellation(TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }

    private MockHttpMessageHandler ServeThenHang(string prefix)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new HangingStream(Encoding.UTF8.GetBytes(prefix))),
        });
        return handler;
    }

    private MockHttpMessageHandler Serve(string body)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))),
        });
        return handler;
    }

    private DeepSeekLlmProvider Provider(int timeoutSeconds, int? idleSeconds, int maxRetries = 0)
    {
        var config = LlmConfig.Create("deepseek-chat") with
        {
            MaxRetries = maxRetries,
            ApiKey = "sk-test",
            TimeoutSeconds = timeoutSeconds,
            StreamIdleSeconds = idleSeconds,
        };
        return new DeepSeekLlmProvider(config, _httpClientFactory, _noOpPolicy, NullLogger<DeepSeekLlmProvider>.Instance);
    }

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
