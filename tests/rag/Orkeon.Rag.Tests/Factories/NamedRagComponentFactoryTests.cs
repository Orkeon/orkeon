using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Factories;

/// <summary>
/// Tests the MemoryProviderFactory-style resolution rules shared by all RAG
/// factories: trim + case-insensitive matching, aliases, and loud failures
/// listing the known names.
/// </summary>
public class NamedRagComponentFactoryTests
{
    private sealed class FakeChunkingStrategy : IChunkingStrategy
    {
        public string Name => "fake";

        public IReadOnlyList<Chunk> Chunk(RagDocument document, ChunkingOptions options) => [];
    }

    [Fact]
    public void Create_WithRegisteredName_ReturnsComponentFromDelegate()
    {
        var factory = new ChunkingStrategyFactory();
        var strategy = new FakeChunkingStrategy();
        factory.Register("fake", () => strategy);

        var resolved = factory.Create("fake");

        Assert.Same(strategy, resolved);
    }

    [Fact]
    public void Create_IsCaseInsensitive_AndTrims()
    {
        var factory = new RerankerFactory();
        var reranker = new StubReranker();
        factory.Register("onnx", () => reranker);

        Assert.Same(reranker, factory.Create("  ONNX  "));
        Assert.Same(reranker, factory.Create("Onnx"));
    }

    [Fact]
    public void Create_WithAlias_ResolvesToSameRegistration()
    {
        var factory = new QueryTransformerFactory();
        var calls = 0;
        factory.Register("multi-query", NewTransformer, "multiquery", "mq");

        Assert.NotNull(factory.Create("multiquery"));
        Assert.NotNull(factory.Create("MQ"));
        Assert.Equal(2, calls);

        IQueryTransformer NewTransformer()
        {
            calls++;
            return new FakeTransformer();
        }
    }

    [Fact]
    public void Create_UnknownName_ThrowsListingKnownAliases_NeverSilent()
    {
        var factory = new ChunkingStrategyFactory();
        factory.Register("recursive", () => new FakeChunkingStrategy(), "default");
        factory.Register("sentence", () => new FakeChunkingStrategy());

        var exception = Assert.Throws<RagComponentNotFoundException>(() => factory.Create("semantic"));

        Assert.Equal("semantic", exception.RequestedName);
        Assert.Contains("chunking strategy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("recursive", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sentence", exception.Message, StringComparison.Ordinal);
        Assert.Contains("default", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, exception.KnownNames.Count);
    }

    [Fact]
    public void Create_OnEmptyFactory_ThrowsExplainingNothingIsRegistered()
    {
        var factory = new RerankerFactory();

        var exception = Assert.Throws<RagComponentNotFoundException>(() => factory.Create("onnx"));

        Assert.Contains("(none registered)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_DuplicateName_Throws_EvenWithDifferentCasing()
    {
        var factory = new ChunkingStrategyFactory();
        factory.Register("recursive", () => new FakeChunkingStrategy());

        Assert.Throws<ArgumentException>(
            () => factory.Register("Recursive", () => new FakeChunkingStrategy()));
    }

    [Fact]
    public void Register_AliasCollidingWithExistingName_Throws()
    {
        var factory = new ChunkingStrategyFactory();
        factory.Register("recursive", () => new FakeChunkingStrategy());

        Assert.Throws<ArgumentException>(
            () => factory.Register("sentence", () => new FakeChunkingStrategy(), "recursive"));
    }

    [Fact]
    public void Register_BlankName_Throws()
    {
        var factory = new ChunkingStrategyFactory();

        Assert.Throws<ArgumentException>(
            () => factory.Register("   ", () => new FakeChunkingStrategy()));
    }

    [Fact]
    public void TryCreate_UnknownName_ReturnsFalse_WithoutThrowing()
    {
        var factory = new RerankerFactory();

        var found = factory.TryCreate("nope", out var component);

        Assert.False(found);
        Assert.Null(component);
    }

    [Fact]
    public void KnownNames_ExposeRegisteredCasing()
    {
        var factory = new RerankerFactory();
        factory.Register("onnx", () => new StubReranker(), "cross-encoder");

        Assert.Contains("onnx", factory.KnownNames);
        Assert.Contains("cross-encoder", factory.KnownNames);
        Assert.True(factory.IsKnown("Cross-Encoder"));
    }

    private sealed class FakeTransformer : IQueryTransformer
    {
        public string Name => "fake";

        public Task<IReadOnlyList<string>> TransformAsync(
            string query,
            QueryTransformContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
