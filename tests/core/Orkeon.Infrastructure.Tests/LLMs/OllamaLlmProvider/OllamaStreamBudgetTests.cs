using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// LLM-12 on Ollama's NDJSON stream: <c>Llm:TimeoutSeconds</c> bounds the whole call,
/// <c>Llm:StreamIdleSeconds</c> the silence between two frames, and a stream closed before
/// <c>done: true</c> with no answer is a failed call rather than an empty answer.
/// </summary>
public class OllamaStreamBudgetTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<OllamaLlmProvider> _logger = new();

    private const string Hel = """{"response":"Hel","done":false}""";
    private const string Lo = """{"response":"lo","done":false}""";
    private const string Done = """{"response":"","done":true,"prompt_eval_count":7,"eval_count":3}""";

    private static TestHttpMessageHandler Serving(Func<Stream> body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });

    private OllamaLlmProvider CreateProvider(LlmConfig config, TestHttpMessageHandler handler)
    {
        _httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient(handler));
        return new OllamaLlmProvider(
            config, _httpClientFactory, new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance), _logger);
    }

    private static LlmConfig Config(int timeoutSeconds = 600, int? idleSeconds = null) =>
        LlmConfig.Create("llama3.2") with
        {
            BaseUrl = new Uri("http://localhost:11434"),
            TimeoutSeconds = timeoutSeconds,
            StreamIdleSeconds = idleSeconds,
            MaxRetries = 0,
        };

    private static Stream Closed(params string[] frames) => new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', frames) + "\n"));

    private static HangingStream Hanging(params string[] frames) => new(Encoding.UTF8.GetBytes(string.Join('\n', frames) + "\n"));

    private static async Task<List<LlmStreamEvent>> CollectAsync(OllamaLlmProvider provider)
    {
        var token = TestContext.Current.CancellationToken;
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync([LlmMessage.User("salut")], null, token).WithCancellation(token))
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
    public async Task A_stream_that_ends_on_done_is_a_clean_answer()
    {
        using var providerHandler = Serving(() => Closed(Hel, Lo, Done));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("Hello", final.Content);
        Assert.Null(final.Error);
        Assert.False(final.Metadata.ContainsKey(LlmResponseMetadataKeys.StreamTruncated));
        Assert.Equal(3, final.CompletionTokens);
    }

    [Fact]
    public async Task A_stream_closed_before_any_answer_is_a_failed_call()
    {
        using var providerHandler = Serving(() => Closed("""{"response":"","done":false}"""));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal(string.Empty, final.Content);
        Assert.Equal("StreamTruncated", final.ErrorType);
        Assert.Contains("Ollama closed the stream before any answer arrived", final.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stream_closed_after_some_answer_is_that_answer_flagged_truncated()
    {
        using var providerHandler = Serving(() => Closed(Hel, Lo));
        using var provider = CreateProvider(Config(), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("Hello", final.Content);
        Assert.Null(final.Error);
        Assert.Equal(true, final.Metadata[LlmResponseMetadataKeys.StreamTruncated]);
    }

    [Fact]
    public async Task A_body_that_never_ends_fails_on_TimeoutSeconds_with_the_partial_content()
    {
        using var providerHandler = Serving(() => Hanging(Hel));
        using var provider = CreateProvider(Config(timeoutSeconds: 1), providerHandler);

        var events = await CollectAsync(provider);

        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta == "Hel");
        var final = Final(events);
        Assert.Equal("Hel", final.Content);
        Assert.Equal(nameof(TaskCanceledException), final.ErrorType);
        Assert.Contains("Ollama did not answer within Llm:TimeoutSeconds = 1 s", final.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_silence_longer_than_StreamIdleSeconds_fails_the_call_and_names_the_setting()
    {
        using var providerHandler = Serving(() => Hanging(Hel));
        using var provider = CreateProvider(Config(timeoutSeconds: 600, idleSeconds: 1), providerHandler);

        var final = Final(await CollectAsync(provider));

        Assert.Equal("StreamIdleTimeout", final.ErrorType);
        Assert.Contains("Llm:StreamIdleSeconds = 1 s", final.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_propagates_as_such()
    {
        using var providerHandler = Serving(() => Hanging(Hel));
        using var provider = CreateProvider(Config(), providerHandler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var collecting = Task.Run(async () =>
        {
            await foreach (var ev in provider.ChatStreamingAsync([LlmMessage.User("salut")], null, cts.Token))
            {
                if (ev.Kind == LlmStreamEventKind.ContentDelta)
                    await cts.CancelAsync();
            }
        }, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collecting);
    }

    [Fact]
    public async Task The_text_stream_fails_on_an_elapsed_bound_and_on_a_stream_cut_before_any_text()
    {
        using var hangingHandler = Serving(() => Hanging(Hel));
        using var hanging = CreateProvider(Config(timeoutSeconds: 1), hangingHandler);
        var timedOut = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in hanging.GenerateStreamingAsync("salut", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Contains("Llm:TimeoutSeconds = 1 s", timedOut.Message, StringComparison.Ordinal);

        using var cutHandler = Serving(() => Closed("""{"response":"","done":false}"""));
        using var cut = CreateProvider(Config(), cutHandler);
        var truncated = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in cut.GenerateStreamingAsync("salut", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Contains("closed the stream before any answer arrived", truncated.Message, StringComparison.Ordinal);

        using var completeHandler = Serving(() => Closed(Hel, Lo, Done));
        using var complete = CreateProvider(Config(), completeHandler);
        var tokens = new List<string>();
        await foreach (var token in complete.GenerateStreamingAsync("salut", cancellationToken: TestContext.Current.CancellationToken))
            tokens.Add(token);
        Assert.Equal(["Hel", "lo"], tokens);
    }
}
