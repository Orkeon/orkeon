using System.Net;
using System.Text;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// A Mistral model that reasons streams <c>delta.content</c> as an ARRAY of typed chunks —
/// the same shape <see cref="MistralReasoningContentParseTests"/> pins on the buffered path —
/// and <c>mistral-large-4</c> reasons by default. Measured 2026-10-07: 123 array deltas for
/// 4 strings on a count to five. The text stream threw <c>InvalidOperationException</c> on
/// the first one (M3), and the chat stream dropped them: the trace, and the first words of
/// the answer, which arrive inside the chunk that closes the thinking.
/// </summary>
public sealed class MistralStreamedContentChunksTests : IDisposable
{
    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "Count from one to five." }];
    private static readonly string[] AnswerDeltas = ["1\n", "2\n3\n", "4\n5"];

    /// <summary>Verbatim shapes of a mistral-large-4 stream (2026-10-07), in the order they arrive.</summary>
    private static readonly string[] Deltas =
    [
        """{"choices":[{"delta":{"role":"assistant","content":""}}]}""",
        """{"choices":[{"delta":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"The user is"}],"closed":true}]}}]}""",
        """{"choices":[{"delta":{"content":[{"type":"thinking","thinking":[{"type":"text","text":" counting."}],"closed":true}]}}]}""",
        """{"choices":[{"delta":{"content":[{"type":"thinking","thinking":[]},{"type":"text","text":"1\n"}]}}]}""",
        """{"choices":[{"delta":{"content":"2\n3\n"}}]}""",
        """{"choices":[{"delta":{"content":"4\n5"}}]}""",
    ];

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public MistralStreamedContentChunksTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SseStream(), Encoding.UTF8, "text/event-stream")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    [Fact]
    public async Task ShouldStreamTheAnswer_WhenATextStreamCarriesChunkedContentDeltas()
    {
        var config = LlmConfig.Create("mistral-large-4", TestApiKey);
        using var provider = new MistralLlmProvider(config, _httpClientFactory, _noOpPolicy);

        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(
            "Count from one to five.", cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        Assert.Equal(AnswerDeltas, tokens);
    }

    [Fact]
    public async Task ShouldKeepTheAnswerAndTheTrace_WhenAChatStreamCarriesChunkedContentDeltas()
    {
        var config = LlmConfig.Create("mistral-large-4", TestApiKey);
        using var provider = new MistralLlmProvider(config, _httpClientFactory, _noOpPolicy);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(
            OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(ev);
        }

        var content = events.Where(e => e.Kind == LlmStreamEventKind.ContentDelta).Select(e => e.Delta).ToList();
        Assert.Equal(AnswerDeltas, content);

        var reasoning = events.Where(e => e.Kind == LlmStreamEventKind.ReasoningDelta).Select(e => e.Delta);
        Assert.Equal("The user is counting.", string.Concat(reasoning));

        var final = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Equal("1\n2\n3\n4\n5", final.Content);
        Assert.Equal("The user is counting.", final.Metadata["reasoning_content"]);
    }

    private static string SseStream()
    {
        var stream = new StringBuilder();
        foreach (var delta in Deltas)
            stream.Append("data: ").Append(delta).Append("\n\n");
        return stream.Append("data: [DONE]\n\n").ToString();
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
