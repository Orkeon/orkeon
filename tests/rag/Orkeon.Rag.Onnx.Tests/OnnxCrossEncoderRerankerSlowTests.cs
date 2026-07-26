using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Factories;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Rag.Onnx.Reranking;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Onnx.Tests;

/// <summary>
/// Real cross-encoder inference over the embedded ms-marco-MiniLM-L-6-v2
/// weights. Tagged Slow: boots the native onnxruntime (which can crash
/// sandboxed test hosts — RAG-04 fiche / SONAR-14 T3); run on a host machine
/// or CI.
/// </summary>
[Trait("Category", "Slow")]
public class OnnxCrossEncoderRerankerSlowTests
{
    private static ScoredChunk MakeChunk(string id, string content) => new()
    {
        Chunk = new Chunk
        {
            Id = id,
            DocumentId = "doc",
            SourceId = "source",
            Content = content,
        },
        Score = 0.5,
        ScoreOrigin = "vector",
    };

    [Fact]
    public async Task Rerank_RelevantPair_ScoresAboveIrrelevantPair()
    {
        using var reranker = new OnnxCrossEncoderReranker(new ThrowingFileSystemService());
        var candidates = new[]
        {
            MakeChunk("irrelevant", "The recipe calls for two cups of flour and a pinch of salt."),
            MakeChunk("relevant", "Berlin has a population of about 3.7 million inhabitants and is the capital of Germany."),
            MakeChunk("noise", "Football matches last ninety minutes plus injury time."),
        };

        var result = await reranker.RerankAsync("How many people live in Berlin?", candidates, topN: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Equal("relevant", result[0].Chunk.Id);
        Assert.All(result, scored => Assert.Equal("cross-encoder", scored.ScoreOrigin));
        Assert.All(result, scored => Assert.InRange(scored.Score, 0.0, 1.0));
        Assert.True(result[0].Score > result[1].Score, "relevant pair must outscore irrelevant pairs");
        Assert.True(result[0].Score > result[^1].Score);
    }

    [Fact]
    public async Task Rerank_TruncatesToTopN_BestFirst()
    {
        using var reranker = new OnnxCrossEncoderReranker(new ThrowingFileSystemService());
        var candidates = new[]
        {
            MakeChunk("a", "Cats sleep during most of the day."),
            MakeChunk("b", "The Eiffel Tower is located in Paris, France."),
            MakeChunk("c", "Bread is baked in an oven."),
            MakeChunk("d", "Paris is the capital city of France, home of the Eiffel Tower."),
        };

        var result = await reranker.RerankAsync("Where is the Eiffel Tower?", candidates, topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Score >= result[1].Score);
        Assert.Contains(result, scored => scored.Chunk.Id is "b" or "d");
    }

    [Fact]
    public async Task FactoryResolution_OnnxAlias_EndToEnd()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new ThrowingFileSystemService());
        services.AddOrkeonRagReranking();
        services.AddOrkeonOnnxReranker();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<RerankerFactory>();

        Assert.True(factory.IsKnown("onnx"));
        Assert.True(factory.IsKnown("cross-encoder"));

        var reranker = factory.Create("cross-encoder");
        Assert.IsType<OnnxCrossEncoderReranker>(reranker);
        Assert.Same(reranker, factory.Create("onnx")); // one shared ONNX session

        IReadOnlyList<ScoredChunk> result = await reranker.RerankAsync(
            "What color is the sky?",
            [MakeChunk("sky", "On a clear day the sky appears blue."), MakeChunk("cake", "The cake was delicious.")],
            topN: 1,
            TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("sky", result[0].Chunk.Id);
    }
}
