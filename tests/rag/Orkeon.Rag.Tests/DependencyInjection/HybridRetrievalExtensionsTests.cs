using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Retrieval;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// Tests of the hybrid-retrieval opt-in (RAG-04/C2): disabled configuration leaves the
/// document store untouched; <c>Orkeon:Rag:Retrieval:Hybrid</c> (section or flat
/// shorthand) wraps the registered <see cref="IDocumentStore"/> — including a
/// host-registered one — in the <see cref="HybridSearchDocumentStore"/> decorator.
/// </summary>
public class HybridRetrievalExtensionsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    private static ServiceProvider BuildRagProvider(IChatClient chatClient, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton(chatClient);
        services.AddOrkeonRag(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddOrkeonRag_HybridDisabledByDefault_StoreIsNotDecorated()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildRagProvider(chat, Config());

        Assert.IsType<FakeDocumentStore>(provider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public void AddOrkeonRag_HybridEnabled_SectionForm_DecoratesTheStore()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildRagProvider(
            chat, Config(("Orkeon:Rag:Retrieval:Hybrid:Enabled", "true")));

        Assert.IsType<HybridSearchDocumentStore>(provider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public void AddOrkeonRag_HybridEnabled_FlatShorthand_DecoratesTheStore()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildRagProvider(
            chat, Config(("Orkeon:Rag:Retrieval:Hybrid", "true")));

        Assert.IsType<HybridSearchDocumentStore>(provider.GetRequiredService<IDocumentStore>());

        var options = provider.GetRequiredService<IOptions<HybridRetrievalOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal(ReciprocalRankFusion.DefaultK, options.RrfK);
    }

    [Fact]
    public void AddOrkeonRag_FlatShorthandFalse_StoreIsNotDecorated()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildRagProvider(
            chat, Config(("Orkeon:Rag:Retrieval:Hybrid", "false")));

        Assert.IsType<FakeDocumentStore>(provider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public void AddOrkeonRag_HybridEnabled_BindsRrfKFromConfiguration()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildRagProvider(chat, Config(
            ("Orkeon:Rag:Retrieval:Hybrid:Enabled", "true"),
            ("Orkeon:Rag:Retrieval:Hybrid:RrfK", "90")));

        Assert.IsType<HybridSearchDocumentStore>(provider.GetRequiredService<IDocumentStore>());
        Assert.Equal(90, provider.GetRequiredService<IOptions<HybridRetrievalOptions>>().Value.RrfK);
    }

    [Fact]
    public void AddOrkeonHybridRetrieval_Enabled_WithoutDocumentStore_FailsLoudly()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddOrkeonHybridRetrieval(Config(("Orkeon:Rag:Retrieval:Hybrid", "true"))));

        Assert.Contains("Orkeon:Rag:Retrieval:Hybrid", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IDocumentStore), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrkeonHybridRetrieval_Disabled_IsAStrictNoOp_OnTheStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());

        services.AddOrkeonHybridRetrieval(Config());

        using var provider = services.BuildServiceProvider();
        Assert.IsType<FakeDocumentStore>(provider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task DecoratedHostStore_KeepsWorking_ThroughTheDecorator()
    {
        var services = new ServiceCollection();
        var hostStore = new StubDocumentStore();
        services.AddSingleton<IDocumentStore>(hostStore);
        services.AddOrkeonHybridRetrieval(Config(("Orkeon:Rag:Retrieval:Hybrid", "true")));

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();

        Assert.IsType<HybridSearchDocumentStore>(store);
        await store.SearchAsync(
            "docs",
            new Orkeon.Rag.Abstractions.Models.RetrievalQuery { Text = "hello" },
            TestContext.Current.CancellationToken);

        Assert.Single(hostStore.SearchCalls); // delegation reaches the host's inner store
    }
}
