using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;
using Xunit;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

/// <summary>
/// Tests for <see cref="AnalysisEmbeddingProviderAdapter"/> — the RAG-01/C3 bridge from the
/// Analysis embedding abstraction (batch-only EmbedBatchAsync) to the Application port.
/// </summary>
public class AnalysisEmbeddingProviderAdapterTests
{
    // --- Construction ---

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AnalysisEmbeddingProviderAdapter(null!));
    }

    [Fact]
    public void Constructor_Defaults_NameIsInnerTypeName_ModelIsDefault()
    {
        var adapter = new AnalysisEmbeddingProviderAdapter(new FakeAnalysisEmbeddingProvider());

        Assert.Equal(nameof(FakeAnalysisEmbeddingProvider), adapter.Name);
        Assert.Equal(AnalysisEmbeddingProviderAdapter.DefaultModel, adapter.Model);
    }

    [Fact]
    public void Constructor_ExplicitNameAndModel_AreExposed()
    {
        var adapter = new AnalysisEmbeddingProviderAdapter(
            new FakeAnalysisEmbeddingProvider(), name: "Local", model: "bge-micro-v2");

        Assert.Equal("Local", adapter.Name);
        Assert.Equal("bge-micro-v2", adapter.Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankNameOrModel_FallsBackToDefaults(string blank)
    {
        var adapter = new AnalysisEmbeddingProviderAdapter(
            new FakeAnalysisEmbeddingProvider(), name: blank, model: blank);

        Assert.Equal(nameof(FakeAnalysisEmbeddingProvider), adapter.Name);
        Assert.Equal(AnalysisEmbeddingProviderAdapter.DefaultModel, adapter.Model);
    }

    // --- Dimensions ---

    [Fact]
    public void Dimensions_DelegatesToInnerProvider()
    {
        var inner = new FakeAnalysisEmbeddingProvider { Dimensions = 384 };
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);

        Assert.Equal(384, adapter.Dimensions);

        inner.Dimensions = 768;
        Assert.Equal(768, adapter.Dimensions);
    }

    // --- Unary adaptation ---

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingAsync_RoutesThroughEmbedBatchAsync_AsSingletonBatch()
    {
        var inner = new FakeAnalysisEmbeddingProvider { Dimensions = 4 };
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);

        var vector = await adapter.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        var batch = Assert.Single(inner.ReceivedBatches);
        Assert.Equal(["hello"], batch);
        Assert.Equal(4, vector.Length);
        Assert.All(vector, v => Assert.Equal(1f, v));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingAsync_NullText_Throws()
    {
        var adapter = new AnalysisEmbeddingProviderAdapter(new FakeAnalysisEmbeddingProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.GetEmbeddingAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingAsync_InnerReturnsEmpty_ReturnsEmptyVector()
    {
        var inner = new FakeAnalysisEmbeddingProvider { VectorsToReturn = [] };
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);

        var vector = await adapter.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Empty(vector);
    }

    // --- Batch adaptation ---

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingsAsync_RoutesThroughEmbedBatchAsync_PreservesOrder()
    {
        var inner = new FakeAnalysisEmbeddingProvider { Dimensions = 3 };
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);

        var vectors = await adapter.GetEmbeddingsAsync(["a", "b", "c"], TestContext.Current.CancellationToken);

        var batch = Assert.Single(inner.ReceivedBatches);
        Assert.Equal(["a", "b", "c"], batch);
        Assert.Equal(3, vectors.Count);
        // Fake fills vector i with (i + 1): order must be preserved through the bridge.
        Assert.All(vectors[0], v => Assert.Equal(1f, v));
        Assert.All(vectors[1], v => Assert.Equal(2f, v));
        Assert.All(vectors[2], v => Assert.Equal(3f, v));
        Assert.All(vectors, v => Assert.Equal(3, v.Length));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingsAsync_EmptyInput_ReturnsEmpty_WithoutCallingInner()
    {
        var inner = new FakeAnalysisEmbeddingProvider();
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);

        var vectors = await adapter.GetEmbeddingsAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(vectors);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingsAsync_NullTexts_Throws()
    {
        var adapter = new AnalysisEmbeddingProviderAdapter(new FakeAnalysisEmbeddingProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.GetEmbeddingsAsync(null!, TestContext.Current.CancellationToken));
    }

    // --- Cancellation propagation ---

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingAsync_PropagatesCancellationToken()
    {
        var inner = new FakeAnalysisEmbeddingProvider();
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);
        using var cts = new CancellationTokenSource();

        await adapter.GetEmbeddingAsync("hello", cts.Token);

        var token = Assert.Single(inner.ReceivedTokens);
        Assert.Equal(cts.Token, token);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingsAsync_PropagatesCancellationToken()
    {
        var inner = new FakeAnalysisEmbeddingProvider();
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);
        using var cts = new CancellationTokenSource();

        await adapter.GetEmbeddingsAsync(["a", "b"], cts.Token);

        var token = Assert.Single(inner.ReceivedTokens);
        Assert.Equal(cts.Token, token);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetEmbeddingsAsync_CancelledToken_SurfacesOperationCanceled()
    {
        var inner = new FakeAnalysisEmbeddingProvider { ThrowIfCancelled = true };
        var adapter = new AnalysisEmbeddingProviderAdapter(inner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.GetEmbeddingsAsync(["a"], cts.Token));
    }
}
