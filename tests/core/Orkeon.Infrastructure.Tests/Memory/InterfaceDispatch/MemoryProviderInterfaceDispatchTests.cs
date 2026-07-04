using System.Reflection;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Memory.InterfaceDispatch;

/// <summary>
/// Regression tests for the default-interface-method trap (MAT-017 / R10.1).
/// C# freezes interface mapping at the class that lists the interface
/// (<c>MemoryProviderBase</c>) and does not re-map members declared by derived classes:
/// before the fix, <c>IMemoryProvider.SearchSimilarAsync</c> resolved to the interface's
/// empty default body on the default DI provider (<see cref="InMemoryProvider"/>), so
/// semantic recall silently returned zero results.
/// </summary>
public class MemoryProviderInterfaceDispatchTests
{
    private static readonly float[] s_unitX = [1f, 0f, 0f];
    private static readonly float[] s_unitY = [0f, 1f, 0f];

    [Fact]
    public async Task ShouldFindStoredItem_WhenSearchSimilarAsyncIsCalledThroughInterface()
    {
        // Arrange - the provider is deliberately typed as the INTERFACE: on the pre-R10.1
        // code this call resolved the empty default interface method and returned nothing.
        IMemoryProvider provider = new InMemoryProvider(new TestLogger<InMemoryProvider>());

        await provider.StoreWithEmbeddingAsync(
            "k1", MemoryItem.Create("semantic recall target"), s_unitX, TestContext.Current.CancellationToken);
        await provider.StoreWithEmbeddingAsync(
            "k2", MemoryItem.Create("orthogonal noise"), s_unitY, TestContext.Current.CancellationToken);

        // Act
        var results = await provider.SearchSimilarAsync(
            s_unitX, topK: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - the stored item is retrieved with a perfect cosine score
        Assert.NotEmpty(results);
        Assert.Equal("semantic recall target", results[0].Item.Content);
        Assert.Equal(1f, results[0].Score, precision: 4);
    }

    [Fact]
    public async Task ShouldRecallStoredItem_WhenCognitiveMemoryServiceUsesInMemoryProvider()
    {
        // Arrange - CognitiveMemoryService holds the provider as IMemoryProvider, which is
        // exactly the dispatch path that used to hit the empty default interface method.
        IMemoryProvider provider = new InMemoryProvider(new TestLogger<InMemoryProvider>());
        var embeddingProvider = new MockEmbeddingProvider();
        embeddingProvider.SetEmbeddingResult(s_unitX);

        var service = CreateCognitiveMemoryService(provider, embeddingProvider);

        var item = MemoryItem.Create("The deployment pipeline uses GitHub Actions", importance: 0.8f);
        await provider.StoreWithEmbeddingAsync(item.Id, item, s_unitX, TestContext.Current.CancellationToken);

        // Act
        var recalled = await service.RecallAsync(
            CrewId.Create(), "How do we deploy?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert - on the pre-R10.1 code this was empty (silent zero-result recall)
        Assert.NotEmpty(recalled);
        Assert.Equal("The deployment pipeline uses GitHub Actions", recalled[0].Item.Content);
        Assert.True(recalled[0].SemanticScore > 0.99f);
    }

    [Fact]
    public void ShouldNotResolveSearchSimilarAsyncToDefaultInterfaceMethod_ForAnyConcreteInfrastructureProvider()
    {
        // Arrange - the default interface method declared by IMemoryProvider itself
        var interfaceType = typeof(IMemoryProvider);
        var interfaceMethod = interfaceType.GetMethod(nameof(IMemoryProvider.SearchSimilarAsync));
        Assert.NotNull(interfaceMethod);

        var providerTypes = typeof(InMemoryProvider).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && interfaceType.IsAssignableFrom(t))
            .ToList();

        // Sanity check: the scan must see the in-repo providers (otherwise the guard is dead)
        Assert.NotEmpty(providerTypes);
        Assert.Contains(typeof(InMemoryProvider), providerTypes);
        Assert.Contains(typeof(EncryptedMemoryProviderDecorator), providerTypes);

        // Act & Assert - for every concrete provider, the interface slot for
        // SearchSimilarAsync must target a method declared by a class, not the
        // interface's own (empty) default implementation.
        foreach (var providerType in providerTypes)
        {
            var map = providerType.GetInterfaceMap(interfaceType);
            var index = Array.IndexOf(map.InterfaceMethods, interfaceMethod);
            Assert.True(index >= 0, $"{providerType.FullName}: SearchSimilarAsync not found in the interface map.");

            var target = map.TargetMethods[index];
            Assert.True(
                target.DeclaringType != interfaceType,
                $"{providerType.FullName} resolves IMemoryProvider.SearchSimilarAsync to the empty " +
                "default interface method: vector search would silently return zero results. " +
                "Override MemoryProviderBase.SearchSimilarAsync (or re-list IMemoryProvider on the " +
                "class declaration with its own implementation) to close the trap (MAT-017 / R10.1).");
        }
    }

    private static CognitiveMemoryService CreateCognitiveMemoryService(
        IMemoryProvider provider,
        MockEmbeddingProvider embeddingProvider)
    {
        var options = Options.Create(new CognitiveMemoryOptions());
        var llmProvider = new MockLlmProvider();

        var analysisServices = new CognitiveAnalysisServices(
            new MemoryAnalyzer(llmProvider, options, new MockLogger<MemoryAnalyzer>()),
            new ContradictionDetector(llmProvider, options, new MockLogger<ContradictionDetector>()),
            new MemoryConsolidator(llmProvider, provider, options, new MockLogger<MemoryConsolidator>()),
            new CompositeScorer(options));

        return new CognitiveMemoryService(
            new MockMemoryService(),
            provider,
            embeddingProvider,
            analysisServices,
            options,
            new MockLogger<CognitiveMemoryService>());
    }
}
