using Microsoft.Extensions.AI;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Bridge tests for <see cref="LocalEmbeddingGenerator"/> (Microsoft.Extensions.AI 10.5.0
/// adapter wrapping <see cref="LocalEmbeddingProvider"/>).
/// </summary>
[Trait("Category", "Slow")] // wraps a real LocalEmbeddingProvider → boots ONNX (~200 ms)
public sealed class LocalEmbeddingGeneratorTests
{
    private static (LocalEmbeddingGenerator generator, LocalEmbeddingProvider provider) CreatePair()
    {
        var provider = new LocalEmbeddingProvider(new FakeFileSystemService(), new LocalEmbeddingOptions());
        var generator = new LocalEmbeddingGenerator(provider);
        return (generator, provider);
    }

    [Fact]
    public void LocalEmbeddingGenerator_Metadata_ReportsCorrectProviderName()
    {
        var (generator, provider) = CreatePair();
        using (provider)
        {
            Assert.Equal("local-bge-micro-v2", generator.Metadata.ProviderName);
            Assert.Equal("bge-micro-v2", generator.Metadata.DefaultModelId);

            // M.E.AI 10.5.0 also exposes Metadata via GetService(Type, object?).
            // The bridge must surface the same Metadata instance through both the property
            // and the GetService(typeof(EmbeddingGeneratorMetadata)) lookup.
            var fromGetService = generator.GetService(typeof(EmbeddingGeneratorMetadata));
            Assert.Same(generator.Metadata, fromGetService);
        }
    }

    [Fact]
    public async Task LocalEmbeddingGenerator_GenerateAsync_PreservesOrder_AndDimensions()
    {
        var (generator, provider) = CreatePair();
        using (provider)
        {
            var inputs = new[] { "first text", "second text" };

            var result = await generator.GenerateAsync(inputs, options: null, CancellationToken.None);

            Assert.Equal(2, result.Count);
            // BGE-micro-v2 produces 384-dim vectors.
            Assert.Equal(384, result[0].Vector.Length);
            Assert.Equal(384, result[1].Vector.Length);

            // Order preserved — single-input round-trip must match the batch entry pos-by-pos.
            var single0 = await generator.GenerateAsync([inputs[0]], options: null, CancellationToken.None);
            var single1 = await generator.GenerateAsync([inputs[1]], options: null, CancellationToken.None);

            Assert.Equal(single0[0].Vector.ToArray(), result[0].Vector.ToArray());
            Assert.Equal(single1[0].Vector.ToArray(), result[1].Vector.ToArray());
        }
    }

    [Fact]
    public async Task LocalEmbeddingGenerator_Dispose_DoesNotDisposeUnderlyingProvider()
    {
        // The bridge's Dispose() must be a no-op — ownership of the provider stays with
        // whoever constructed it (typically the DI container). Disposing the bridge
        // MUST NOT tear down the inner ONNX session.
        var (generator, provider) = CreatePair();
        using (provider)
        {
            generator.Dispose();

            // Provider still works after bridge.Dispose().
            var vectors = await provider.EmbedBatchAsync(["still alive"], CancellationToken.None);
            Assert.Single(vectors);
            Assert.Equal(384, vectors[0].Length);
        }
    }
}
