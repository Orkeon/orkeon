using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Provider-core tests for <see cref="LocalEmbeddingProvider"/>.
/// All tests instantiate a real <see cref="SmartComponents.LocalEmbeddings.LocalEmbedder"/>
/// (boot ONNX ~200 ms) and are therefore tagged Slow.
/// </summary>
[Trait("Category", "Slow")]
public sealed class LocalEmbeddingProviderTests
{
    private static LocalEmbeddingProvider CreateDefaultProvider() =>
        new(new FakeFileSystemService(), new LocalEmbeddingOptions());

    [Fact]
    public async Task EmbedBatch_Empty_ReturnsEmpty()
    {
        using var provider = CreateDefaultProvider();

        var result = await provider.EmbedBatchAsync([], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedBatch_Single_ReturnsCorrectDimensions()
    {
        using var provider = CreateDefaultProvider();

        var result = await provider.EmbedBatchAsync(["hello world"], CancellationToken.None);

        Assert.Single(result);
        // BGE-micro-v2 produces 384-dim vectors.
        Assert.Equal(384, result[0].Length);
    }

    [Fact]
    public async Task EmbedBatch_Multiple_PreservesOrder()
    {
        using var provider = CreateDefaultProvider();

        var inputs = new[] { "apple banana", "cat dog", "rocket science" };

        var batch = await provider.EmbedBatchAsync(inputs, CancellationToken.None);

        // Compute single-input embeddings for each text and verify positional alignment.
        var single0 = await provider.EmbedBatchAsync([inputs[0]], CancellationToken.None);
        var single1 = await provider.EmbedBatchAsync([inputs[1]], CancellationToken.None);
        var single2 = await provider.EmbedBatchAsync([inputs[2]], CancellationToken.None);

        Assert.Equal(3, batch.Count);
        Assert.Equal(single0[0].ToArray(), batch[0].ToArray());
        Assert.Equal(single1[0].ToArray(), batch[1].ToArray());
        Assert.Equal(single2[0].ToArray(), batch[2].ToArray());

        // And confirm the three vectors are not all identical.
        Assert.NotEqual(batch[1].ToArray(), batch[0].ToArray());
        Assert.NotEqual(batch[2].ToArray(), batch[1].ToArray());
    }

    [Fact]
    public async Task EmbedBatch_Truncates_When_MaxTextChars_Set()
    {
        const int Cap = 1000;
        using var capped = new LocalEmbeddingProvider(new FakeFileSystemService(), new LocalEmbeddingOptions { MaxTextChars = Cap });
        using var uncapped = new LocalEmbeddingProvider(new FakeFileSystemService(), new LocalEmbeddingOptions { MaxTextChars = null });

        var longText = new string('a', 5000);
        var manuallyTruncated = longText[..Cap];

        var capResult = await capped.EmbedBatchAsync([longText], CancellationToken.None);
        var refResult = await uncapped.EmbedBatchAsync([manuallyTruncated], CancellationToken.None);

        var capVec = capResult[0].ToArray();
        var refVec = refResult[0].ToArray();

        // A 5000-char input capped at 1000 must yield the exact same vector as a
        // manually-truncated 1000-char input.
        Assert.True(capVec.SequenceEqual(refVec));
    }

    [Fact]
    public async Task EmbedBatch_Cancellation_Throws()
    {
        using var provider = CreateDefaultProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.EmbedBatchAsync(["hello"], cts.Token));
    }

    [Fact]
    public void Dimensions_Property_Returns_384()
    {
        using var provider = CreateDefaultProvider();

        // Default zero-config provider uses BGE-micro-v2.
        Assert.Equal(384, provider.Dimensions);
    }

    [Fact]
    public async Task Dispose_Releases_Embedder()
    {
        var provider = CreateDefaultProvider();
        provider.Dispose();

        // Upstream contract validation: SmartComponents.LocalEmbeddings.LocalEmbedder
        // does not document a guaranteed exception type after Dispose(). We therefore
        // assert that ANY exception is raised — the precise type is irrelevant; what
        // matters is that the disposed embedder refuses to serve a new call.
        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.EmbedBatchAsync(["hello"], CancellationToken.None));
    }
}
