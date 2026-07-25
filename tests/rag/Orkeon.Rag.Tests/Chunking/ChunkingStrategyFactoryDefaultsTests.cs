using Orkeon.Rag.Chunking;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>
/// Tests the default registration of the four built-in chunking strategies
/// (RAG-02/C3): names, aliases, semantic embedder wiring, and loud failure on
/// unknown names.
/// </summary>
public class ChunkingStrategyFactoryDefaultsTests
{
    [Theory]
    [InlineData("recursive", typeof(RecursiveChunkingStrategy))]
    [InlineData("recursive_text", typeof(RecursiveChunkingStrategy))]
    [InlineData("default", typeof(RecursiveChunkingStrategy))]
    [InlineData("RECURSIVE", typeof(RecursiveChunkingStrategy))]
    [InlineData("sentence", typeof(SentenceChunkingStrategy))]
    [InlineData("sentences", typeof(SentenceChunkingStrategy))]
    [InlineData("structural", typeof(StructuralChunkingStrategy))]
    [InlineData("markdown", typeof(StructuralChunkingStrategy))]
    [InlineData("headings", typeof(StructuralChunkingStrategy))]
    [InlineData("semantic", typeof(SemanticChunkingStrategy))]
    [InlineData("  Semantic  ", typeof(SemanticChunkingStrategy))]
    public void CreateDefault_ResolvesBuiltInNamesAndAliases(string name, Type expectedType)
    {
        var factory = ChunkingStrategyFactoryDefaults.CreateDefault();

        var strategy = factory.Create(name);

        Assert.IsType(expectedType, strategy);
    }

    [Fact]
    public void CreateDefault_UnknownName_FailsLoudlyListingKnownNames()
    {
        var factory = ChunkingStrategyFactoryDefaults.CreateDefault();

        var ex = Assert.Throws<RagComponentNotFoundException>(() => factory.Create("does-not-exist"));

        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
        Assert.Contains("recursive", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDefault_WiresSemanticEmbedder()
    {
        var callCount = 0;
        float[] Embedder(string _)
        {
            callCount++;
            return [1f, 0f];
        }

        var factory = ChunkingStrategyFactoryDefaults.CreateDefault(Embedder);
        var semantic = factory.Create("semantic");

        var doc = ChunkingTestHelper.Doc("One sentence. Two sentence.");
        semantic.Chunk(doc, new Orkeon.Rag.Abstractions.Options.ChunkingOptions());

        Assert.True(callCount > 0, "the semantic strategy should use the provided embedder");
    }

    [Fact]
    public void RegisterDefaultStrategies_AllowsThirdPartyRegistrationAlongside()
    {
        var factory = new ChunkingStrategyFactory().RegisterDefaultStrategies();

        factory.Register("custom", static () => new RecursiveChunkingStrategy(["|", ""]));

        Assert.True(factory.IsKnown("custom"));
        Assert.True(factory.IsKnown("recursive"));
        Assert.IsType<RecursiveChunkingStrategy>(factory.Create("custom"));
    }

    [Fact]
    public void RegisterDefaultStrategies_Twice_FailsLoudly()
    {
        var factory = ChunkingStrategyFactoryDefaults.CreateDefault();

        Assert.Throws<ArgumentException>(() => factory.RegisterDefaultStrategies());
    }
}
