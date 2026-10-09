using System.Net;
using System.Text;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// LLM-12 on the Messages API stream: <c>Llm:TimeoutSeconds</c> bounds the whole call, headers
/// and body; <c>Llm:StreamIdleSeconds</c> bounds the silence between two lines; a stream closed
/// before <c>message_stop</c> with no answer is a failed call, and one closed after some answer
/// is that answer flagged <c>stream_truncated</c>. A reasoning model that thinks for minutes
/// used to hang a Studio run past any timeout, then come back as an empty answer.
/// </summary>
public class AnthropicStreamBudgetTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<AnthropicLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly LlmMessage[] Messages = [LlmMessage.User("hello")];

    private const string Start = """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""";
    private const string Hel = """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hel"}}""";
    private const string Lo = """data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"lo"}}""";
    private const string Thinking = """data: {"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"pondering"}}""";
    private const string Stop = """data: {"type":"message_stop"}""";

    private static string Sse(params string[] frames) => string.Join("\n\n", frames) + "\n\n";

    private static TestHttpMessageHandler Serving(Func<Stream> body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });

    private AnthropicLlmProvider CreateProvider(LlmConfig config, TestHttpMessageHandler handler)
    {
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        return new AnthropicLlmProvider(config, _httpClientFactory, _noOpPolicy, _logger);
    }

    private static LlmConfig Config(int timeoutSeconds = 600, int? idleSeconds = null) =>
        LlmConfig.Create("claude-sonnet-5", TestApiKey) with { TimeoutSeconds = timeoutSeconds, StreamIdleSeconds = idleSeconds, MaxRetries = 0 };

    private static Stream Closed(params string[] frames) => new MemoryStream(Encoding.UTF8.GetBytes(Sse(frames)));

    private static HangingStream Hanging(params string[] frames) => new(Encoding.UTF8.GetBytes(Sse(frames)));

    private static async Task<List<LlmStreamEvent>> CollectAsync(AnthropicLlmProvider provider, CancellationToken? token = null)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Messages, cancellationToken: token ?? TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }

    private static LlmResponse Final(List<LlmStreamEvent> events)
    {
        var last = events[^1];
        Assert.Equal(LlmStreamEventKind.Completed, last.Kind);
        return last.FinalResponse!;
    }

    [Fact]
    public async Task A_stream_that_ends_on_message_stop_is_a_clean_answer()
    {
        using var providerHandler = Serving(() => Closed(Start, Hel, Lo, Stop));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("Hello", final.Content);
        Assert.Null(final.Error);
        Assert.False(final.Metadata.ContainsKey(LlmResponseMetadataKeys.StreamTruncated));
    }

    [Fact]
    public async Task A_stream_closed_before_any_answer_is_a_failed_call_that_names_the_reasoning_it_streamed()
    {
        using var providerHandler = Serving(() => Closed(Start, Thinking));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal(string.Empty, final.Content);
        Assert.Equal("StreamTruncated", final.ErrorType);
        Assert.Contains("Anthropic closed the stream before any answer arrived", final.Error, StringComparison.Ordinal);
        Assert.Contains("9 characters of reasoning", final.Error, StringComparison.Ordinal);
        Assert.Null(final.RawResponseBody);
    }

    [Fact]
    public async Task A_stream_closed_after_some_answer_is_that_answer_flagged_truncated()
    {
        using var providerHandler = Serving(() => Closed(Start, Hel));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("Hel", final.Content);
        Assert.Null(final.Error);
        Assert.Equal(true, final.Metadata[LlmResponseMetadataKeys.StreamTruncated]);
    }

    [Fact]
    public async Task A_body_that_never_ends_fails_on_TimeoutSeconds_with_the_partial_content()
    {
        using var providerHandler = Serving(() => Hanging(Start, Hel));
        using var provider = CreateProvider(Config(timeoutSeconds: 1), providerHandler);

        var events = await CollectAsync(provider);

        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta == "Hel");
        var final = Final(events);
        Assert.Equal("Hel", final.Content);
        Assert.Equal(nameof(TaskCanceledException), final.ErrorType);
        Assert.Contains("Anthropic did not answer within Llm:TimeoutSeconds = 1 s", final.Error, StringComparison.Ordinal);
        Assert.Null(final.RawResponseBody);
    }

    [Fact]
    public async Task A_silence_longer_than_StreamIdleSeconds_fails_the_call_and_names_the_setting()
    {
        using var providerHandler = Serving(() => Hanging(Start, Hel));
        using var provider = CreateProvider(Config(timeoutSeconds: 600, idleSeconds: 1), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("StreamIdleTimeout", final.ErrorType);
        Assert.Contains("Llm:StreamIdleSeconds = 1 s", final.Error, StringComparison.Ordinal);
        Assert.Equal("Hel", final.Content);
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_propagates_as_such()
    {
        using var providerHandler = Serving(() => Hanging(Start, Hel));
        using var provider = CreateProvider(Config(), providerHandler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var collecting = Task.Run(async () =>
        {
            await foreach (var ev in provider.ChatStreamingAsync(Messages, cancellationToken: cts.Token))
            {
                if (ev.Kind == LlmStreamEventKind.ContentDelta)
                    await cts.CancelAsync();
            }
        }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collecting);
    }

    [Fact]
    public async Task A_vendor_error_event_after_the_200_fails_the_call_like_a_refusal()
    {
        const string error = """data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";
        using var providerHandler = Serving(() => Closed(Start, Hel, error, Lo, Stop));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("APIError", final.ErrorType);
        Assert.Equal("Anthropic API error: overloaded_error - Overloaded", final.Error);
        Assert.Equal("Hel", final.Content);
    }

    [Fact]
    public async Task The_text_stream_fails_on_TimeoutSeconds_with_the_same_sentence()
    {
        using var providerHandler = Serving(() => Hanging(Start, Hel));
        using var provider = CreateProvider(Config(timeoutSeconds: 1), providerHandler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Contains("Llm:TimeoutSeconds = 1 s", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_text_stream_closed_before_any_text_fails_and_one_closed_after_text_ends_normally()
    {
        using var emptyHandler = Serving(() => Closed(Start, Thinking));
        using var empty = CreateProvider(Config(), emptyHandler);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in empty.GenerateStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Contains("closed the stream before any answer arrived", ex.Message, StringComparison.Ordinal);

        using var partialHandler = Serving(() => Closed(Start, Hel));
        using var partial = CreateProvider(Config(), partialHandler);
        var tokens = new List<string>();
        await foreach (var token in partial.GenerateStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            tokens.Add(token);
        Assert.Equal(["Hel"], tokens);
    }
}
