using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Analysis.Vectors;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// SONAR-14: pins the infrastructure-side RaggableTree wiring — the logged embedding
/// provider replacement for OpenAI and Ollama, the missing-key guard, and the
/// pass-through when no embedding provider is configured.
/// </summary>
public class RaggableTreeInfrastructureExtensionsTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void WithoutAnEmbeddingProvider_TheWiringIsAPassThrough()
    {
        var services = NewServices();
        var options = new RaggableTreeOptions();

        var returned = services.AddRaggableTreeWithLogging(options);

        Assert.Same(services, returned);
    }

    [Fact]
    public void OpenAI_ReplacesTheEmbeddingProvider_WithALoggedSingleton()
    {
        var services = NewServices();
        var options = new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.OpenAI,
                ApiKey = "sk-test",
            },
        };

        services.AddRaggableTreeWithLogging(options);

        using var provider = services.BuildServiceProvider();
        var embedding = provider.GetRequiredService<IEmbeddingProvider>();
        Assert.IsType<OpenAIEmbeddingProvider>(embedding);
        // Singleton: resolved twice, same instance.
        Assert.Same(embedding, provider.GetRequiredService<IEmbeddingProvider>());
    }

    [Fact]
    public void OpenAI_WithoutAnApiKey_FailsFastAtRegistrationTime()
    {
        var services = NewServices();
        var options = new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.OpenAI },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddRaggableTreeWithLogging(options));
        Assert.Contains("ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ollama_ReplacesTheEmbeddingProvider_WithALoggedSingleton()
    {
        var services = NewServices();
        var options = new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProviderKind.Ollama,
                BaseUrl = new Uri("http://localhost:11434/"),
            },
        };

        services.AddRaggableTreeWithLogging(options);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<OllamaEmbeddingProvider>(provider.GetRequiredService<IEmbeddingProvider>());
    }

    [Fact]
    public void TheExtension_GuardsItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RaggableTreeInfrastructureExtensions.AddRaggableTreeWithLogging(null!, new RaggableTreeOptions()));
        Assert.Throws<ArgumentNullException>(() =>
            NewServices().AddRaggableTreeWithLogging(null!));
    }
}
