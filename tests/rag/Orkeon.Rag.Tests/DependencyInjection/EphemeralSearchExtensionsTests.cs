using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Search;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// Tests for <see cref="EphemeralSearchExtensions.AddOrkeonEphemeralSearch"/>: the
/// facade search is decided from the registrations and resolved lazily, and an
/// unconfigured subsystem fails with an actionable message only when searched.
/// </summary>
public class EphemeralSearchExtensionsTests
{
    private static EphemeralSearchRequest Request(params SourceDescriptor[] sources) => new()
    {
        Sources = [.. sources],
        Query = "what is inside",
        TopK = 3,
    };

    [Fact]
    public void ShouldThrow_WhenServicesIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => EphemeralSearchExtensions.AddOrkeonEphemeralSearch(null!));
    }

    [Fact]
    public void ShouldRegisterInlineTextLoaderOnce_WhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddOrkeonEphemeralSearch();
        services.AddOrkeonEphemeralSearch();

        using var provider = services.BuildServiceProvider();
        var loader = Assert.Single(provider.GetServices<IDocumentLoader>());
        Assert.IsType<InlineTextLoader>(loader);
    }

    [Fact]
    public async Task ShouldThrowActionableMessageOnSearch_WhenRagIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddOrkeonEphemeralSearch();
        using var provider = services.BuildServiceProvider();

        // Resolving never fails: only a search does.
        var search = provider.GetRequiredService<IEphemeralCollectionSearch>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => search.SearchAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Contains("AddOrkeonRag", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IEmbeddingProvider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldResolveDependenciesAtFirstSearchOnly_WhenRagIsRegistered()
    {
        var resolutions = 0;
        var store = new StubDocumentStore();
        var pipeline = new FakeIngestionPipeline();
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingProvider>(_ => { resolutions++; return new FakeEmbeddingProvider(); });
        services.AddSingleton<IIngestionPipeline>(_ => { resolutions++; return pipeline; });
        services.AddSingleton<IDocumentStore>(_ => { resolutions++; return store; });
        services.AddOrkeonEphemeralSearch();
        using var provider = services.BuildServiceProvider();

        var search = provider.GetRequiredService<IEphemeralCollectionSearch>();
        Assert.Equal(0, resolutions);

        var result = await search.SearchAsync(
            Request(new SourceDescriptor { Location = "hello world", Kind = "text" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, resolutions);
        Assert.StartsWith("eph-", result.Collection, StringComparison.Ordinal);
        Assert.Single(pipeline.Requests);
        var call = Assert.Single(store.SearchCalls);
        Assert.Equal(result.Collection, call.Collection);
        Assert.Equal(3, call.Query.TopK);

        // The inner service is built once and reused.
        await search.SearchAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(3, resolutions);
    }

    [Fact]
    public void ShouldBuildServiceEagerly_WhenProviderLacksIsServiceAndAllDependenciesExist()
    {
        var provider = new StubServiceProvider
        {
            Services =
            {
                [typeof(IEmbeddingProvider)] = new FakeEmbeddingProvider(),
                [typeof(IIngestionPipeline)] = new FakeIngestionPipeline(),
                [typeof(IDocumentStore)] = new StubDocumentStore(),
            },
        };

        var search = InvokeFactory(provider);

        Assert.IsType<EphemeralCollectionSearchService>(search);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task ShouldFallBackToUnconfigured_WhenProviderLacksIsServiceAndADependencyIsMissing(
        bool embeddings, bool pipeline, bool store)
    {
        var provider = new StubServiceProvider();
        if (embeddings) provider.Services[typeof(IEmbeddingProvider)] = new FakeEmbeddingProvider();
        if (pipeline) provider.Services[typeof(IIngestionPipeline)] = new FakeIngestionPipeline();
        if (store) provider.Services[typeof(IDocumentStore)] = new StubDocumentStore();

        var search = InvokeFactory(provider);

        Assert.IsNotType<EphemeralCollectionSearchService>(search);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => search.SearchAsync(Request(), TestContext.Current.CancellationToken));
    }

    private static IEphemeralCollectionSearch InvokeFactory(IServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddOrkeonEphemeralSearch();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IEphemeralCollectionSearch));
        Assert.NotNull(descriptor.ImplementationFactory);
        return Assert.IsType<IEphemeralCollectionSearch>(descriptor.ImplementationFactory(provider), exactMatch: false);
    }

    /// <summary>A provider that does not implement IServiceProviderIsService.</summary>
    private sealed class StubServiceProvider : IServiceProvider
    {
        public Dictionary<Type, object> Services { get; } = [];

        public object? GetService(Type serviceType) => Services.GetValueOrDefault(serviceType);
    }
}
