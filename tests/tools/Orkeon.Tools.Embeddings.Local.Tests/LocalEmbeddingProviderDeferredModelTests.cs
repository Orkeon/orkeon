using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Tests.Shared.FileSystem;
using SmartComponents.LocalEmbeddings;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// A host without the local ONNX model starts; only the first embedding fails.
/// Only throwing or counting factories are used: the real model is never loaded.
/// </summary>
public sealed class LocalEmbeddingProviderDeferredModelTests
{
    private const string MissingModelMessage = "model.onnx is missing";

    private static LocalEmbeddingProvider CreateProviderWithFactory(Func<string?, LocalEmbedder> factory) =>
        new(new FakeFileSystemService(), new LocalEmbeddingOptions(), null, factory);

    private static LocalEmbedder ThrowMissingModel(string? _) =>
        throw new FileNotFoundException(MissingModelMessage);

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldConstruct_WhenTheModelFactoryThrows()
    {
        var ex = Record.Exception(() => CreateProviderWithFactory(ThrowMissingModel).Dispose());

        Assert.Null(ex);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldReportEmbeddedDimensions_WithoutLoadingTheModel()
    {
        var dimensions = 0;
        var ex = Record.Exception(() =>
        {
            using var provider = CreateProviderWithFactory(ThrowMissingModel);
            dimensions = provider.Dimensions;
        });

        Assert.Null(ex);
        Assert.Equal(384, dimensions);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldThrowNamingTheModelAndTheExclusion_WhenEmbeddingWithoutTheModel()
    {
        var inner = new FileNotFoundException(MissingModelMessage);
        LocalEmbeddingProvider? provider = null;
        var ctorEx = Record.Exception(() => provider = CreateProviderWithFactory(_ => throw inner));
        Assert.Null(ctorEx);
        using var owned = provider;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider!.EmbedBatchAsync(["hello"], CancellationToken.None));

        Assert.Contains("LocalEmbeddingsModel/default/model.onnx", ex.Message);
        Assert.Contains("ExcludeAssets=\"build\"", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldNameTheCustomModelPath_WhenEmbeddingWithoutThatModel()
    {
        const string CustomPath = "/models/custom.onnx";
        var inner = new FileNotFoundException(MissingModelMessage);
        using var provider = new LocalEmbeddingProvider(
            new FakeFileSystemService(),
            new LocalEmbeddingOptions { ModelPath = CustomPath },
            null,
            _ => throw inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedBatchAsync(["hello"], CancellationToken.None));

        Assert.Contains(CustomPath, ex.Message);
        Assert.DoesNotContain("ExcludeAssets=\"build\"", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldBuildTheEmbedderOnce_WhenEmbeddingsRaceOnFirstUse()
    {
        const int Racers = 8;
        var inFlight = 0;
        var maxInFlight = 0;
        using var neverSet = new ManualResetEventSlim(false);
        using var provider = CreateProviderWithFactory(_ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            // Holds the load open so a second builder, if the provider lets one in, overlaps it. A racer
            // arriving after this attempt failed legitimately starts a new one: the count of loads depends
            // on timing, the overlap does not.
            neverSet.Wait(TimeSpan.FromMilliseconds(200));
            Interlocked.Decrement(ref inFlight);
            throw new FileNotFoundException(MissingModelMessage);
        });
        using var start = new Barrier(Racers);

        var racers = Enumerable.Range(0, Racers).Select(_ => Task.Factory.StartNew(
            async () =>
            {
                start.SignalAndWait();
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => provider.EmbedBatchAsync(["hello"], CancellationToken.None));
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap()).ToArray();
        await Task.WhenAll(racers);

        Assert.Equal(1, Volatile.Read(ref maxInFlight));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }

    // Needs the real embedded model (the factory returns a loaded embedder): Slow, like LocalEmbeddingProviderTests.
    [Fact]
    [Trait("BR", "CH-03")]
    [Trait("Category", "Slow")]
    public async Task ShouldReleaseTheEmbedderAndRefuse_WhenDisposedWhileTheModelLoads()
    {
        using var loading = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var provider = CreateProviderWithFactory(_ =>
        {
            loading.Set();
            release.Wait();
            return new LocalEmbedder();
        });

        var call = Task.Run(() => provider.EmbedBatchAsync(["hello"], CancellationToken.None));
        loading.Wait(TestContext.Current.CancellationToken);
        provider.Dispose();
        release.Set();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => call);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public void ShouldDisposeWithoutLoadingTheModel_WhenNothingWasEmbedded()
    {
        var calls = 0;
        var ex = Record.Exception(() =>
        {
            var provider = CreateProviderWithFactory(_ =>
            {
                calls++;
                throw new FileNotFoundException(MissingModelMessage);
            });
            provider.Dispose();
        });

        Assert.Null(ex);
        Assert.Equal(0, calls);
    }
}
