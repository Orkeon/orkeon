using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Embeddings;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

/// <summary>FWK-25: <c>Orkeon:Embeddings:Provider</c> picks Mistral, Azure OpenAI or Gemini.</summary>
public class EmbeddingResolverTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] pairs)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build());
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static IEmbeddingProvider Inner(IEmbeddingProvider port) =>
        Assert.IsType<TracedEmbeddingProvider>(port).Inner;

    [Theory]
    [InlineData("mistral", typeof(MistralEmbeddingProvider))]
    [InlineData("azure-openai", typeof(AzureOpenAIEmbeddingProvider))]
    [InlineData("gemini", typeof(GeminiEmbeddingProvider))]
    [InlineData("Gemini", typeof(GeminiEmbeddingProvider))]
    public void ShouldPickTheProviderNamedInTheSettings(string name, Type expected)
    {
        using var sp = Build(("Orkeon:Embeddings:Provider", name), ("Orkeon:Embeddings:EnableCache", "false"));

        var port = sp.GetRequiredService<IEmbeddingProvider>();

        Assert.IsType(expected, Inner(port));
    }

    [Fact]
    public void ShouldStartAcceptingTheNewNames_WhenSettingsAreValidated()
    {
        using var sp = Build(("Orkeon:Embeddings:Provider", "mistral"));

        var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        Assert.Null(DefaultEmbeddingProviderResolver.ProviderProblem(options));
    }

    [Fact]
    public void ShouldListTheSixProviders_WhenTheNameIsUnknown()
    {
        var problem = DefaultEmbeddingProviderResolver.ProviderProblem(new EmbeddingOptions { Provider = "cohere" });

        Assert.NotNull(problem);
        Assert.Contains("mistral", problem, StringComparison.Ordinal);
        Assert.Contains("azure-openai", problem, StringComparison.Ordinal);
        Assert.Contains("gemini", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldCarryTheConfiguredModel_WhenMistralIsPicked()
    {
        using var sp = Build(
            ("Orkeon:Embeddings:Provider", "mistral"),
            ("Orkeon:Embeddings:Model", "mistral-embed"),
            ("Orkeon:Embeddings:Dimension", "1024"),
            ("Orkeon:Embeddings:EnableCache", "false"));

        var inner = Inner(sp.GetRequiredService<IEmbeddingProvider>());

        Assert.Equal("mistral-embed", inner.Model);
        Assert.Equal(1024, inner.Dimensions);
    }
}
