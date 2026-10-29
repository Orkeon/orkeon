using System.Collections.Immutable;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Reranking;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Reranking;

public class DecisionModelRerankerTests
{
    private static ScoredChunk MakeChunk(int index, double score) => new()
    {
        Chunk = new Chunk
        {
            Id = $"chunk-{index}",
            DocumentId = "doc",
            SourceId = "source",
            Content = $"content {index}",
            Index = index,
        },
        Score = score,
        ScoreOrigin = "vector",
    };

    private static FakeDecisionModel Answering(params double[] relevance) => new()
    {
        Respond = _ => new DecisionResponse
        {
            Model = "jev",
            Answers = relevance
                .Select((p, i) => (Id: $"p{i}", Answer: new NoulAnswer { Noul = p }))
                .ToImmutableDictionary(a => a.Id, DecisionAnswer (a) => a.Answer),
        },
    };

    [Fact]
    public async Task Rerank_OrdersByTheProbabilityOfRelevance_InOneRequest()
    {
        var model = Answering(0.1, 0.9, 0.5);
        var candidates = new[] { MakeChunk(0, 0.9), MakeChunk(1, 0.8), MakeChunk(2, 0.7) };

        var result = await new DecisionModelReranker(model).RerankAsync("what is X?", candidates, topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal(["chunk-1", "chunk-2"], result.Select(r => r.Chunk.Id));
        Assert.Equal(0.9, result[0].Score);
        Assert.Equal(DecisionModelReranker.ScoreOrigin, result[0].ScoreOrigin);
        var request = Assert.Single(model.Requests);
        Assert.Equal(3, request.Questions.Count);
        Assert.Equal("what is X?", request.State["query"]!.GetValue<string>());
        Assert.Equal("content 2", request.State["passages"]!["p2"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rerank_AsksNothing_WhenThereIsNoCandidate()
    {
        var model = Answering();

        var result = await new DecisionModelReranker(model).RerankAsync("q", [], topN: 3, TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void Name_IsDecision()
    {
        Assert.Equal("decision", new DecisionModelReranker(Answering()).Name);
    }
}
