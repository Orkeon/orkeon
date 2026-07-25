using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Tests;

/// <summary>
/// Contract-shape tests for the flagship DTOs: safe defaults (no nulls in
/// collections) and value semantics.
/// </summary>
public class RagAnswerTests
{
    [Fact]
    public void RagAnswer_Defaults_AreEmptyButNeverNull()
    {
        var answer = new RagAnswer { Text = "42 [1]" };

        Assert.Equal("42 [1]", answer.Text);
        Assert.NotNull(answer.Citations);
        Assert.Empty(answer.Citations);
        Assert.NotNull(answer.Trace);
        Assert.Empty(answer.Trace.Steps);
        Assert.Empty(answer.Trace.QueryVariants);
        Assert.Empty(answer.Trace.Verdicts);
        Assert.Equal(0, answer.Trace.Iterations);
        Assert.Null(answer.Trace.Route);
    }

    [Fact]
    public void RetrievalQuery_Defaults_UseCandidateCascade()
    {
        var query = new RetrievalQuery { Text = "q" };

        // 50 → 5 cascade (plan §5.1): stores return 50 candidates by default…
        Assert.Equal(50, query.TopK);

        // …and the pipeline keeps 5 for the context by default.
        var ragQuery = new RagQuery { Text = "q", Collection = "docs" };
        Assert.Equal(5, ragQuery.TopN);
    }

    [Fact]
    public void ScoredChunk_PreservesScore_AndOrigin()
    {
        var chunk = new Chunk
        {
            Id = "c1",
            DocumentId = "d1",
            SourceId = "s1",
            Content = "hello",
        };

        var scored = new ScoredChunk { Chunk = chunk, Score = 0.87, ScoreOrigin = "vector" };

        Assert.Equal(0.87, scored.Score);
        Assert.Equal("vector", scored.ScoreOrigin);
        Assert.Same(chunk, scored.Chunk);
    }

    [Fact]
    public void RetrievalVerdict_CarriesGrade_AndPerChunkRelevance()
    {
        var verdict = new RetrievalVerdict
        {
            Grade = RetrievalGrade.Ambiguous,
            ChunkRelevances = [new ChunkRelevance { ChunkId = "c1", Relevance = 0.4 }],
        };

        Assert.Equal(RetrievalGrade.Ambiguous, verdict.Grade);
        var relevance = Assert.Single(verdict.ChunkRelevances);
        Assert.Equal("c1", relevance.ChunkId);
        Assert.Equal(0.4, relevance.Relevance);
    }
}
