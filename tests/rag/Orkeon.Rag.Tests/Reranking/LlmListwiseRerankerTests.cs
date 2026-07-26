using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Reranking;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Reranking;

public class LlmListwiseRerankerTests
{
    private static ScoredChunk MakeChunk(int index) => new()
    {
        Chunk = new Chunk
        {
            Id = $"chunk-{index}",
            DocumentId = "doc",
            SourceId = "source",
            Content = $"passage number {index}",
            Index = index,
        },
        Score = 1.0 - (index * 0.1),
        ScoreOrigin = "vector",
    };

    private static ScoredChunk[] MakeCandidates(int count) =>
        [.. Enumerable.Range(0, count).Select(MakeChunk)];

    [Fact]
    public void Name_IsLlm()
    {
        using var chatClient = new FakeChatClient();
        var reranker = new LlmListwiseReranker(chatClient);
        Assert.Equal("llm", reranker.Name);
        Assert.Equal(LlmListwiseReranker.RerankerName, reranker.Name);
    }

    [Fact]
    public async Task Rerank_ValidJsonOrder_ReordersAndScoresByNormalizedRank()
    {
        using var chatClient = new FakeChatClient { ResponseText = "[3, 1, 2]" };
        var reranker = new LlmListwiseReranker(chatClient);
        var candidates = MakeCandidates(3);

        var result = await reranker.RerankAsync("query", candidates, topN: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Equal("chunk-2", result[0].Chunk.Id);
        Assert.Equal("chunk-0", result[1].Chunk.Id);
        Assert.Equal("chunk-1", result[2].Chunk.Id);

        // Normalized descending rank: 3/3, 2/3, 1/3.
        Assert.Equal(1.0, result[0].Score, precision: 10);
        Assert.Equal(2.0 / 3.0, result[1].Score, precision: 10);
        Assert.Equal(1.0 / 3.0, result[2].Score, precision: 10);
        Assert.All(result, scored => Assert.Equal("llm-rerank", scored.ScoreOrigin));
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task Rerank_JsonEmbeddedInProse_IsStillParsed()
    {
        using var chatClient = new FakeChatClient { ResponseText = "Sure! The ranking is: [2, 1] — hope this helps." };
        var reranker = new LlmListwiseReranker(chatClient);

        var result = await reranker.RerankAsync("query", MakeCandidates(2), topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal("chunk-1", result[0].Chunk.Id);
        Assert.Equal("chunk-0", result[1].Chunk.Id);
    }

    [Fact]
    public async Task Rerank_TruncatesToTopN()
    {
        using var chatClient = new FakeChatClient { ResponseText = "[5, 4, 3, 2, 1]" };
        var reranker = new LlmListwiseReranker(chatClient);

        var result = await reranker.RerankAsync("query", MakeCandidates(5), topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("chunk-4", result[0].Chunk.Id);
        Assert.Equal("chunk-3", result[1].Chunk.Id);
        Assert.Equal(1.0, result[0].Score, precision: 10);
        Assert.Equal(0.5, result[1].Score, precision: 10);
    }

    [Fact]
    public async Task Rerank_MalformedResponse_FallsBackToOriginalOrder_AndLogsWarning()
    {
        using var chatClient = new FakeChatClient { ResponseText = "I cannot rank these passages, sorry." };
        var logger = new CollectingLogger<LlmListwiseReranker>();
        var reranker = new LlmListwiseReranker(chatClient, logger);
        var candidates = MakeCandidates(3);

        var result = await reranker.RerankAsync("query", candidates, topN: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Equal("chunk-0", result[0].Chunk.Id);
        Assert.Equal("chunk-1", result[1].Chunk.Id);
        Assert.Equal("chunk-2", result[2].Chunk.Id);
        Assert.All(result, scored => Assert.Equal("llm-rerank", scored.ScoreOrigin));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Rerank_OutOfRangeAndDuplicateIndices_AreDropped_MissingAppended()
    {
        // 99 is out of range, the second 2 is a duplicate; 1 and 3 were omitted
        // by the model and must be appended in their original order.
        using var chatClient = new FakeChatClient { ResponseText = "[99, 2, 2]" };
        var reranker = new LlmListwiseReranker(chatClient);

        var result = await reranker.RerankAsync("query", MakeCandidates(3), topN: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Equal("chunk-1", result[0].Chunk.Id);
        Assert.Equal("chunk-0", result[1].Chunk.Id);
        Assert.Equal("chunk-2", result[2].Chunk.Id);
    }

    [Fact]
    public async Task Rerank_EmptyCandidatesOrNonPositiveTopN_ShortCircuitsWithoutLlmCall()
    {
        using var chatClient = new FakeChatClient();
        var reranker = new LlmListwiseReranker(chatClient);

        Assert.Empty(await reranker.RerankAsync("query", [], topN: 5, TestContext.Current.CancellationToken));
        Assert.Empty(await reranker.RerankAsync("query", MakeCandidates(2), topN: 0, TestContext.Current.CancellationToken));
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task Rerank_PromptContainsQueryAndNumberedPassages()
    {
        using var chatClient = new FakeChatClient { ResponseText = "[1, 2]" };
        var reranker = new LlmListwiseReranker(chatClient);

        await reranker.RerankAsync("what is orkeon?", MakeCandidates(2), topN: 2, TestContext.Current.CancellationToken);

        Assert.NotNull(chatClient.LastMessages);
        var user = chatClient.LastMessages![^1].Text;
        Assert.Contains("what is orkeon?", user, StringComparison.Ordinal);
        Assert.Contains("[1] passage number 0", user, StringComparison.Ordinal);
        Assert.Contains("[2] passage number 1", user, StringComparison.Ordinal);
        Assert.Equal(0f, chatClient.LastOptions?.Temperature);
    }

    [Theory]
    [InlineData("[2,1,3]", 3, new[] { 1, 0, 2 })]
    [InlineData("ranking: 2 then 1", 2, new[] { 1, 0 })]
    [InlineData("[]", 2, null)]
    [InlineData("no digits here", 2, null)]
    [InlineData("[\"a\",\"b\"]", 2, null)]
    public void ParseOrder_ToleratesRealWorldOutputs(string response, int count, int[]? expected)
    {
        var order = LlmListwiseReranker.ParseOrder(response, count);

        if (expected is null)
        {
            Assert.Null(order);
        }
        else
        {
            Assert.NotNull(order);
            Assert.Equal(expected, order);
        }
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
