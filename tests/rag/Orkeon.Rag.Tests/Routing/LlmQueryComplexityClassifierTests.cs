using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Routing;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Routing;

/// <summary>
/// Tests of the constrained LLM classifier (RAG-05/C3): clean JSON for the 3
/// routes, JSON wrapped in prose, synonyms and case-insensitivity, unusable
/// response → safe SingleShot fallback with a warning, JSON response-format
/// constraint on the chat call, and cancellation.
/// </summary>
public class LlmQueryComplexityClassifierTests
{
    [Theory]
    [InlineData("{\"route\": \"no_retrieval\"}", QueryRoute.NoRetrieval)]
    [InlineData("{\"route\": \"single_shot\"}", QueryRoute.SingleShot)]
    [InlineData("{\"route\": \"iterative\"}", QueryRoute.Iterative)]
    public async Task Classify_CleanJson_ReturnsTheRoute(string response, QueryRoute expected)
    {
        using var chatClient = new FakeChatClient { ResponseText = response };
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        var route = await classifier.ClassifyAsync("some query", TestContext.Current.CancellationToken);

        Assert.Equal(expected, route);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task Classify_JsonWrappedInProseAndCodeFence_IsParsed()
    {
        using var chatClient = new FakeChatClient
        {
            ResponseText = "Sure! Here is my classification:\n```json\n{\"route\": \"iterative\"}\n```\nHope this helps.",
        };
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        var route = await classifier.ClassifyAsync("some query", TestContext.Current.CancellationToken);

        Assert.Equal(QueryRoute.Iterative, route);
    }

    [Theory]
    // Synonyms in the JSON value.
    [InlineData("{\"route\": \"none\"}", QueryRoute.NoRetrieval)]
    [InlineData("{\"route\": \"direct\"}", QueryRoute.NoRetrieval)]
    [InlineData("{\"route\": \"single\"}", QueryRoute.SingleShot)]
    [InlineData("{\"route\": \"simple\"}", QueryRoute.SingleShot)]
    [InlineData("{\"route\": \"multi_hop\"}", QueryRoute.Iterative)]
    [InlineData("{\"route\": \"complex\"}", QueryRoute.Iterative)]
    // Case-insensitivity (property name and value) + dash/space separators.
    [InlineData("{\"Route\": \"SINGLE_SHOT\"}", QueryRoute.SingleShot)]
    [InlineData("{\"ROUTE\": \"No-Retrieval\"}", QueryRoute.NoRetrieval)]
    [InlineData("{\"route\": \"multi hop\"}", QueryRoute.Iterative)]
    // Bare route word, no JSON at all (last-resort tolerance).
    [InlineData("iterative", QueryRoute.Iterative)]
    [InlineData("The route is: single_shot.", QueryRoute.SingleShot)]
    // Nested object still yields the inner route.
    [InlineData("{\"result\": {\"route\": \"iterative\"}}", QueryRoute.Iterative)]
    public async Task Classify_SynonymsAndTolerantShapes_AreAccepted(string response, QueryRoute expected)
    {
        using var chatClient = new FakeChatClient { ResponseText = response };
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        var route = await classifier.ClassifyAsync("some query", TestContext.Current.CancellationToken);

        Assert.Equal(expected, route);
    }

    [Theory]
    [InlineData("I am not sure how to classify this question, sorry.")]
    [InlineData("{\"route\": \"banana\"}")]
    [InlineData("{\"verdict\": \"yes\"}")]
    [InlineData("")]
    public async Task Classify_UnusableResponse_FallsBackToSingleShot_AndLogsWarning(string response)
    {
        using var chatClient = new FakeChatClient { ResponseText = response };
        var logger = new CollectingLogger<LlmQueryComplexityClassifier>();
        var classifier = new LlmQueryComplexityClassifier(chatClient, logger);

        var route = await classifier.ClassifyAsync("some query", TestContext.Current.CancellationToken);

        // Safe fallback: one unnecessary retrieval costs less than an ungrounded answer.
        Assert.Equal(QueryRoute.SingleShot, route);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Classify_ConstrainsTheCall_JsonResponseFormat_ZeroTemperature_FewShotPrompt()
    {
        using var chatClient = new FakeChatClient { ResponseText = "{\"route\": \"single_shot\"}" };
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        await classifier.ClassifyAsync("what is orkeon?", TestContext.Current.CancellationToken);

        Assert.NotNull(chatClient.LastOptions);
        Assert.IsType<ChatResponseFormatJson>(chatClient.LastOptions!.ResponseFormat);
        Assert.Equal(0f, chatClient.LastOptions.Temperature);

        Assert.NotNull(chatClient.LastMessages);
        var system = chatClient.LastMessages![0].Text;
        Assert.Contains("no_retrieval", system, StringComparison.Ordinal);
        Assert.Contains("single_shot", system, StringComparison.Ordinal);
        Assert.Contains("iterative", system, StringComparison.Ordinal);
        Assert.Contains("what is orkeon?", chatClient.LastMessages[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Classify_WhitespaceQuery_ShortCircuitsToNoRetrieval_WithoutLlmCall()
    {
        using var chatClient = new FakeChatClient();
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        var route = await classifier.ClassifyAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(QueryRoute.NoRetrieval, route);
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task Classify_CancelledToken_Throws_WithoutLlmCall()
    {
        using var chatClient = new FakeChatClient();
        var classifier = new LlmQueryComplexityClassifier(chatClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => classifier.ClassifyAsync("some query", cts.Token));
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task Classify_NullQuery_Throws()
    {
        using var chatClient = new FakeChatClient();
        var classifier = new LlmQueryComplexityClassifier(chatClient);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => classifier.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_NullChatClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LlmQueryComplexityClassifier(null!));
    }

    [Theory]
    [InlineData("{not json at all", null)]
    [InlineData("{\"route\": 42}", null)]
    [InlineData("{} {\"route\": \"iterative\"}", QueryRoute.Iterative)]
    public void ParseRoute_ToleratesRealWorldOutputs(string response, QueryRoute? expected)
    {
        Assert.Equal(expected, LlmQueryComplexityClassifier.ParseRoute(response));
    }

    /// <summary>Hand-written logger double recording every log entry.</summary>
    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
