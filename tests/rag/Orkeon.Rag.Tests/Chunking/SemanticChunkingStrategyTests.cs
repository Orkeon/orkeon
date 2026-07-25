using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>
/// Tests for <see cref="SemanticChunkingStrategy"/>: embedding-driven boundaries when an
/// embedding function is provided, and the documented structural fallback when it is not.
/// </summary>
public class SemanticChunkingStrategyTests
{
    /// <summary>Toy embedder: "cat" sentences → x-axis, "sky" sentences → y-axis.</summary>
    private static float[] TopicEmbedder(string sentence) =>
        sentence.Contains("cat", StringComparison.OrdinalIgnoreCase)
            ? [1f, 0f]
            : [0f, 1f];

    [Fact]
    public void Name_IsSemantic_WithAndWithoutEmbedder()
    {
        Assert.Equal("semantic", new SemanticChunkingStrategy().Name);
        Assert.Equal("semantic", new SemanticChunkingStrategy(TopicEmbedder).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Chunk_WhitespaceOnlyContent_ReturnsEmpty(string content)
    {
        var strategy = new SemanticChunkingStrategy(TopicEmbedder);

        Assert.Empty(strategy.Chunk(ChunkingTestHelper.Doc(content), new ChunkingOptions()));
    }

    [Fact]
    public void Chunk_WithoutEmbedder_FallsBackToStructuralSemantics()
    {
        var doc = ChunkingTestHelper.Doc(
            "# Intro\n\nSome text.\n\n## Section A\n\nContent A.\n\n## Section B\n\nContent B.");
        var options = new ChunkingOptions { MaxChunkSize = 5000 };

        var semantic = new SemanticChunkingStrategy().Chunk(doc, options);
        var structural = new StructuralChunkingStrategy().Chunk(doc, options);

        Assert.Equal(structural.Count, semantic.Count);
        for (int i = 0; i < structural.Count; i++)
        {
            Assert.Equal(structural[i].Content, semantic[i].Content);
            Assert.Equal(structural[i].StartOffset, semantic[i].StartOffset);
            Assert.Equal(structural[i].EndOffset, semantic[i].EndOffset);
        }
    }

    [Fact]
    public void Chunk_WithEmbedder_SplitsAtTopicShift()
    {
        var doc = ChunkingTestHelper.Doc(
            "The cat purrs. The cat sleeps all day. The sky is blue. The sky has clouds.");
        var strategy = new SemanticChunkingStrategy(TopicEmbedder);

        var chunks = strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 1000 });

        Assert.Equal(2, chunks.Count);
        Assert.Contains("cat purrs", chunks[0].Content, StringComparison.Ordinal);
        Assert.Contains("cat sleeps", chunks[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("sky", chunks[0].Content, StringComparison.Ordinal);
        Assert.Contains("sky is blue", chunks[1].Content, StringComparison.Ordinal);
        Assert.Contains("sky has clouds", chunks[1].Content, StringComparison.Ordinal);
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }

    [Fact]
    public void Chunk_WithEmbedder_RespectsMaxChunkSizeEvenOnSameTopic()
    {
        var doc = ChunkingTestHelper.Doc(
            "The cat purrs loudly today. The cat sleeps on the warm mat. The cat eats fish every day.");
        var strategy = new SemanticChunkingStrategy(TopicEmbedder);

        var chunks = strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40 });

        Assert.True(chunks.Count > 1, "same-topic text must still split when MaxChunkSize is exceeded");
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 40));
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }

    [Fact]
    public void Chunk_SimilarityThresholdExtension_OverridesDefault()
    {
        var doc = ChunkingTestHelper.Doc(
            "The cat purrs. The sky is blue.");
        var strategy = new SemanticChunkingStrategy(TopicEmbedder);

        // Threshold -1 accepts any drift → everything stays in one chunk.
        var permissive = strategy.Chunk(doc, new ChunkingOptions
        {
            MaxChunkSize = 1000,
            Extensions = ImmutableDictionary<string, string>.Empty
                .Add(SemanticChunkingStrategy.SimilarityThresholdKey, "-1")
        });

        // Default threshold (0.75) splits at the orthogonal topic shift.
        var strict = strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 1000 });

        Assert.Single(permissive);
        Assert.Equal(2, strict.Count);
    }
}
