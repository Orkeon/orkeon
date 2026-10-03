using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// GAP-40, decisions 2 and 6 — every section <c>AddOrkeonRag</c> binds is validated when a host starts
/// (<see cref="IStartupValidator"/>, which <c>StartAsync</c> runs): the profile, and the names the
/// effective options carry — reranker, query transformer, context ordering, classifier, chunking
/// strategy, store provider — against what this host registered. They were checked at the first query
/// or ingestion, after the model had been called; a <c>balanced</c> profile without the ONNX reranker
/// failed there, on « Unknown reranker 'onnx' ».
/// </summary>
public sealed class RagSettingsValidationTests
{
    private static ServiceProvider Container(Action<IServiceCollection>? host, params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(_ => new FakeChatClient());
        host?.Invoke(services);
        services.AddOrkeonRag(configuration);
        return services.BuildServiceProvider();
    }

    private static string Refusal(ServiceProvider container) =>
        Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IStartupValidator>().Validate()).Message;

    [Theory]
    [InlineData("Orkeon:Rag:Profile", "turbo", "Orkeon:Rag:Profile", "corrective")]
    [InlineData("Orkeon:Rag:Rerank:Kind", "cohere", "Orkeon:Rag:Rerank:Kind", "listwise")]
    [InlineData("Orkeon:Rag:QueryTransform:Mode", "fusion", "Orkeon:Rag:QueryTransform:Mode", "hyde")]
    [InlineData("Orkeon:Rag:Context:Ordering", "middle", "Orkeon:Rag:Context:Ordering", "linear")]
    [InlineData("Orkeon:Rag:Retrieval:TopK", "0", "Orkeon:Rag:Retrieval:TopK", "above zero")]
    [InlineData("Orkeon:Rag:QueryRouting:Classifier", "smart", "Orkeon:Rag:QueryRouting:Classifier", "heuristic")]
    [InlineData("Orkeon:Rag:Ingestion:DefaultChunkingStrategy", "paragraph", "Orkeon:Rag:Ingestion:DefaultChunkingStrategy", "semantic")]
    [InlineData("Orkeon:Rag:Provider", "mongo", "Orkeon:Rag:Provider", "lancedb")]
    [InlineData("Orkeon:Rag:Retrieval:Hybrid", "yes", "Orkeon:Rag:Retrieval:Hybrid", "true or false")]
    public void A_name_or_a_value_the_host_cannot_honour_fails_the_start(string key, string value, string named, string known)
    {
        using var container = Container(host: null, (key, value));

        var refusal = Refusal(container);

        Assert.Contains(named, refusal, StringComparison.Ordinal);
        Assert.Contains(known, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_balanced_profile_fails_the_start_of_a_host_without_the_onnx_reranker_naming_it()
    {
        using var container = Container(host: null, ("Orkeon:Rag:Profile", "balanced"));

        var refusal = Refusal(container);

        Assert.Contains("onnx", refusal, StringComparison.Ordinal);
        Assert.Contains("balanced", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_balanced_profile_starts_a_host_that_offers_onnx()
    {
        using var container = Container(
            services => services.AddSingleton<Orkeon.Rag.Reranking.IRerankerRegistrar>(new StubOnnxRerankerRegistrar()),
            ("Orkeon:Rag:Profile", "balanced"));

        container.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Lancedb_without_its_endpoint_fails_the_start_naming_the_key()
    {
        using var container = Container(host: null, ("Orkeon:Rag:Provider", "lancedb"));

        Assert.Contains("Orkeon:LanceDb:Endpoint", Refusal(container), StringComparison.Ordinal);
    }
}
