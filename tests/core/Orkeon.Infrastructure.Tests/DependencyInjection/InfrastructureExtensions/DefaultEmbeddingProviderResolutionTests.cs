using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Rag.Embeddings;
using AnalysisEmbeddingProvider = Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// RAG-01/C4 — default resolution of the Application port <see cref="IEmbeddingProvider"/> :
/// local (Analysis-side) provider → configured remote provider → fail-fast at first use.
/// The hash stub must never be resolved implicitly.
/// </summary>
public class DefaultEmbeddingProviderResolutionTests
{
    private static ServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    // ── 1. Local / Analysis-side provider present ────────────────────────

    [Fact]
    public async Task ShouldAdaptAnalysisProvider_WhenAnalysisSideProviderIsRegistered()
    {
        // Arrange
        var fake = new FakeAnalysisEmbeddingProvider();
        var services = CreateBaseServices();
        services.AddOrkeonInfrastructure();
        services.AddSingleton<AnalysisEmbeddingProvider>(fake);
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();
        var vector = await port.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        // Assert — adapter chosen, calls routed to the Analysis-side inner provider
        Assert.IsType<AnalysisEmbeddingProviderAdapter>(port);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(fake.Dimensions, vector.Length);
    }

    [Fact]
    public void ShouldPreferLocalAnalysisProvider_OverConfiguredRemoteProvider()
    {
        // Arrange — both a local Analysis-side provider AND an Orkeon:Embeddings config
        var services = CreateBaseServices();
        services.AddSingleton(BuildConfiguration(
            ("Orkeon:Embeddings:Provider", "ollama"),
            ("Orkeon:Embeddings:EnableCache", "false")));
        services.AddOrkeonInfrastructure();
        services.AddSingleton<AnalysisEmbeddingProvider>(new FakeAnalysisEmbeddingProvider());
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert — resolution order: local BGE first
        Assert.IsType<AnalysisEmbeddingProviderAdapter>(port);
    }

    // ── 2. Remote provider configured under Orkeon:Embeddings ────────────

    [Fact]
    public void ShouldUseOllamaProvider_WhenConfiguredWithoutCache()
    {
        // Arrange
        var services = CreateBaseServices();
        services.AddSingleton(BuildConfiguration(
            ("Orkeon:Embeddings:Provider", "ollama"),
            ("Orkeon:Embeddings:Model", "nomic-embed-text"),
            ("Orkeon:Embeddings:EnableCache", "false")));
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert
        var ollama = Assert.IsType<OllamaEmbeddingProvider>(port);
        Assert.Equal("nomic-embed-text", ollama.Model);
    }

    [Fact]
    public void ShouldWrapConfiguredProviderInCache_WhenEnableCacheIsDefault()
    {
        // Arrange — EnableCache defaults to true (parity with AddOrkeonVectorSearch)
        var services = CreateBaseServices();
        services.AddSingleton(BuildConfiguration(
            ("Orkeon:Embeddings:Provider", "ollama")));
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert
        Assert.IsType<CachedEmbeddingProvider>(port);
    }

    [Fact]
    public void ShouldUseOpenAIProvider_WhenConfiguredAndGeneratorIsRegistered()
    {
        // Arrange
        using var generator = new MockEmbeddingGenerator();
        var services = CreateBaseServices();
        services.AddSingleton(BuildConfiguration(
            ("Orkeon:Embeddings:Provider", "openai"),
            ("Orkeon:Embeddings:EnableCache", "false")));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(generator);
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert
        Assert.IsType<OpenAIEmbeddingProvider>(port);
    }

    [Fact]
    public async Task ShouldFailFastAtFirstUse_WhenOpenAIConfiguredWithoutGenerator()
    {
        // Arrange — config declares openai but no M.E.AI generator exists in the container
        var services = CreateBaseServices();
        services.AddSingleton(BuildConfiguration(
            ("Orkeon:Embeddings:Provider", "openai"),
            ("Orkeon:Embeddings:EnableCache", "false")));
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act — resolution itself must not throw
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert — the throw is deferred to first use, with a generator-specific message
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => port.GetEmbeddingAsync("x", TestContext.Current.CancellationToken));
        Assert.Contains("IEmbeddingGenerator", ex.Message, StringComparison.Ordinal);
    }

    // ── 3. Nothing configured → fail-fast at first use ───────────────────

    [Fact]
    public void ShouldResolveWithoutThrowing_WhenNothingIsConfigured()
    {
        // Arrange
        var services = CreateBaseServices();
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act — no exception at container build nor at port resolution
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert
        Assert.IsType<UnconfiguredEmbeddingProvider>(port);
    }

    [Fact]
    public async Task ShouldThrowActionableMessage_AtFirstEmbedCall_WhenNothingIsConfigured()
    {
        // Arrange
        var services = CreateBaseServices();
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Act / Assert — exact actionable message from the RAG-01 fiche, on both methods
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => port.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken));
        Assert.Contains(
            "no semantic embedding provider is configured; add AddOrkeonLocalEmbeddings() or configure Orkeon:Embeddings",
            ex1.Message,
            StringComparison.Ordinal);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => port.GetEmbeddingsAsync(["a", "b"], TestContext.Current.CancellationToken));
        Assert.Contains(
            "no semantic embedding provider is configured; add AddOrkeonLocalEmbeddings() or configure Orkeon:Embeddings",
            ex2.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNeverResolveHashStubImplicitly_WhenNothingIsConfigured()
    {
        // Arrange
        var services = CreateBaseServices();
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert — the silent hash fallback is dead (RAG-01/C4)
        Assert.IsNotType<Orkeon.Infrastructure.Stubs.HashBasedEmbeddingProvider>(port);
    }

    // ── Host overrides & non-regression ───────────────────────────────────

    [Fact]
    public void ShouldRespectHostRegistration_WhenHostRegistersItsOwnProvider()
    {
        // Arrange — an explicit host registration (e.g. the hash stub as a test double) wins
        var mock = new MockEmbeddingProvider();
        var services = CreateBaseServices();
        services.AddSingleton<IEmbeddingProvider>(mock);
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Act
        var port = provider.GetRequiredService<IEmbeddingProvider>();

        // Assert
        Assert.Same(mock, port);
    }

    [Fact]
    public void ShouldStillBuildContainer_WhenAddOrkeonInfrastructureIsCalled()
    {
        // Arrange / Act — non-regression: the DI graph still composes and core services resolve
        var services = CreateBaseServices();
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<ILlmProviderFactory>());
        Assert.NotNull(provider.GetService<IEmbeddingProvider>());
    }
}
