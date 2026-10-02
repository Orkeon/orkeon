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
        // Arrange - the cognitive memory reaches the provider as IMemoryProvider (through the
        // crew's memory), exactly the dispatch path that used to hit the empty default interface
        // method. What it recalls it remembered itself, in the crew's scope (GAP-30): never an
        // entry stored straight into the provider — another crew's, or a RAG chunk.
        IMemoryProvider provider = new InMemoryProvider(new TestLogger<InMemoryProvider>());
        var embeddingProvider = new MockEmbeddingProvider();
        embeddingProvider.SetEmbeddingResult(s_unitX);
        var registry = new Orkeon.Application.Memory.CrewMemoryProviderRegistry();
        var crewId = CrewId.Create();
        registry.Record(crewId, providerType: null, "ops-crew", memoryEnabled: true);
        using var memory = new Orkeon.Application.Memory.MemoryService(
            new StubMemoryProviderFactory(provider), Microsoft.Extensions.Logging.Abstractions.NullLogger<Orkeon.Application.Memory.MemoryService>.Instance,
            registry, provider);
        var service = CreateCognitiveMemoryService(memory, embeddingProvider);

        var unscoped = MemoryItem.Create("An entry of nobody's memory", importance: 0.8f);
        await provider.StoreWithEmbeddingAsync(unscoped.Id, unscoped, s_unitX, TestContext.Current.CancellationToken);
        await service.RememberAsync(crewId, "The deployment pipeline uses GitHub Actions", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var recalled = await service.RecallAsync(
            crewId, "How do we deploy?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert - on the pre-R10.1 code this was empty (silent zero-result recall)
        var only = Assert.Single(recalled);
        Assert.Equal("The deployment pipeline uses GitHub Actions", only.Item.Content);
        Assert.True(only.SemanticScore > 0.99f);
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
        Orkeon.Application.Interfaces.Services.IMemoryService memory,
        MockEmbeddingProvider embeddingProvider)
    {
        var options = Options.Create(new CognitiveMemoryOptions { EnableLlmAnalysis = false, EnableContradictionDetection = false });
        var llmProvider = new MockLlmProvider();

        var analysisServices = new CognitiveAnalysisServices(
            new MemoryAnalyzer(llmProvider, options, new MockLogger<MemoryAnalyzer>()),
            new ContradictionDetector(llmProvider, options, new MockLogger<ContradictionDetector>()),
            new MemoryConsolidator(llmProvider, embeddingProvider, options, new MockLogger<MemoryConsolidator>()),
            new CompositeScorer(options));

        return new CognitiveMemoryService(
            memory,
            embeddingProvider,
            analysisServices,
            options,
            new MockLogger<CognitiveMemoryService>());
    }
}
