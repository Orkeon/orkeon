using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Tests.Doubles;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Rag.Tests.DependencyInjection;

/// <summary>
/// Tests for <see cref="RagServiceCollectionExtensions.AddOrkeonRag"/> —
/// the self-sufficient opt-in of the new RAG subsystem (TryAdd*, host wins).
/// </summary>
public class RagServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    private static ServiceProvider BuildProvider(
        IChatClient chatClient,
        Action<IServiceCollection>? hostRegistrations = null,
        IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton(chatClient);
        hostRegistrations?.Invoke(services);

        services.AddOrkeonRag(configuration ?? EmptyConfiguration());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddOrkeonRag_ChunkingFactory_ShipsTheFourCanonicalStrategies()
    {
        // Regression (RAG-03): a bare factory made every first ingestion fail with
        // "Unknown chunking strategy 'recursive'".
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        var factory = provider.GetRequiredService<ChunkingStrategyFactory>();

        Assert.True(factory.IsKnown("recursive"));
        Assert.True(factory.IsKnown("sentence"));
        Assert.True(factory.IsKnown("structural"));
        Assert.True(factory.IsKnown("semantic"));
        Assert.NotNull(factory.Create("recursive"));
    }

    [Fact]
    public void AddOrkeonRag_RegistersTheKnowledgeContextAugmenter()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        Assert.NotNull(provider.GetService<IKnowledgeContextAugmenter>());
    }

    [Fact]
    public void AddOrkeonRag_RegistersTheFiveLoaders()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        var loaders = provider.GetServices<IDocumentLoader>().ToList();

        Assert.Equal(5, loaders.Count);
        Assert.Contains(loaders, l => l is TextFileLoader);
        Assert.Contains(loaders, l => l is CsvDocumentLoader);
        Assert.Contains(loaders, l => l is HtmlDocumentLoader);
        Assert.Contains(loaders, l => l is PdfDocumentLoader);
        Assert.Contains(loaders, l => l is WebPageLoader);
    }

    [Fact]
    public void AddOrkeonRag_DefaultsTheDocumentStore_ToMemoryProviderDocumentStore()
    {
        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<Orkeon.Domain.Memory.IMemoryProvider>(
            new Stores.Doubles.FakeMemoryProvider());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRag(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();

        // Since RAG-04/C4 the store is always the hybrid decorator (ingestion
        // feeds the BM25 index; searching fuses only per query/profile).
        var store = Assert.IsType<Orkeon.Rag.Retrieval.HybridSearchDocumentStore>(
            provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<Orkeon.Rag.Stores.MemoryProviderDocumentStore>(store.Inner);
    }

    [Fact]
    public void AddOrkeonRag_HostDocumentStoreWins_OverTheMemoryProviderDefault()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat, services =>
            services.AddSingleton<Orkeon.Domain.Memory.IMemoryProvider>(
                new Stores.Doubles.FakeMemoryProvider()));

        // BuildProvider registered FakeDocumentStore before AddOrkeonRag: it must
        // win as the decorated inner store.
        var store = Assert.IsType<Orkeon.Rag.Retrieval.HybridSearchDocumentStore>(
            provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<FakeDocumentStore>(store.Inner);
    }

    [Fact]
    public void AddOrkeonRag_ResolvesPipelinesAndValidation()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        Assert.IsType<DefaultIngestionPipeline>(provider.GetRequiredService<IIngestionPipeline>());
        Assert.IsType<StagedRagPipeline>(provider.GetRequiredService<IRagPipeline>());
        Assert.IsType<ProfileRagPipelineResolver>(provider.GetRequiredService<IRagProfileResolver>());
        Assert.NotNull(provider.GetRequiredService<DataValidationPipeline>());
        Assert.NotNull(provider.GetRequiredService<DocumentLoaderFactory>());
        Assert.NotNull(provider.GetRequiredService<ChunkingStrategyFactory>());
        Assert.Equal(2, provider.GetServices<IDataValidator>().Count());
    }

    [Fact]
    public void AddOrkeonRag_ProfileResolver_MemoizesPresetPipelines_AndServesTheDefault()
    {
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat);

        var resolver = provider.GetRequiredService<IRagProfileResolver>();

        var fast = Assert.IsType<StagedRagPipeline>(resolver.Resolve("fast"));
        Assert.Equal("fast", fast.Options.Profile);
        Assert.Same(fast, resolver.Resolve("fast"));

        var balanced = Assert.IsType<StagedRagPipeline>(resolver.Resolve("balanced"));
        Assert.Equal("balanced", balanced.Options.Profile);
        Assert.NotSame(fast, balanced);

        // 'default' serves the registered IRagPipeline singleton.
        Assert.Same(provider.GetRequiredService<IRagPipeline>(), resolver.Resolve("default"));
    }

    [Fact]
    public void AddOrkeonRag_IsIdempotent()
    {
        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<IDocumentStore>(new FakeDocumentStore());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRag(EmptyConfiguration());
        services.AddOrkeonRag(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(5, provider.GetServices<IDocumentLoader>().Count());
        Assert.Equal(2, provider.GetServices<IDataValidator>().Count());
    }

    [Fact]
    public void AddOrkeonRag_HostRegistrationWins()
    {
        var hostPipeline = new HostRagPipeline();
        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat, services =>
            services.AddSingleton<IRagPipeline>(hostPipeline));

        Assert.Same(hostPipeline, provider.GetRequiredService<IRagPipeline>());
    }

    [Fact]
    public void AddOrkeonRag_BindsOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Ingestion:DefaultChunkingStrategy"] = "sentence",
                ["Orkeon:Rag:Profile"] = "balanced",
                ["Orkeon:Rag:Retrieval:CandidateK"] = "13",
            })
            .Build();

        using var chat = new FakeChatClient();
        using var provider = BuildProvider(chat, configuration: configuration);

        var ingestion = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RagIngestionOptions>>().Value;
        var ragOptions = provider.GetRequiredService<Orkeon.Rag.Abstractions.Options.RagOptions>();

        Assert.Equal("sentence", ingestion.DefaultChunkingStrategy);
        Assert.Equal("balanced", ragOptions.Profile);
        Assert.Equal(13, ragOptions.Retrieval.CandidateK); // override over the preset's 50
        Assert.True(ragOptions.Rerank.Enabled); // preset value survives
    }

    // ---------------------------------------------------------------- Orkeon:Rag:Provider

    [Fact]
    public void AddOrkeonRag_ProviderOption_ResolvesTheStoreProviderThroughTheFactory()
    {
        var factory = new FakeMemoryProviderFactory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Provider"] = "ChromaDB",
                ["Orkeon:Rag:ConnectionString"] = "http://chroma:8000",
                ["Orkeon:Rag:ProviderOptions:ApiKey"] = "secret",
            })
            .Build();

        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory>(factory);
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRag(configuration);

        using var provider = services.BuildServiceProvider();
        var store = Assert.IsType<Orkeon.Rag.Retrieval.HybridSearchDocumentStore>(
            provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<Orkeon.Rag.Stores.MemoryProviderDocumentStore>(store.Inner);

        // The provider resolver runs twice by design: once for the inner store,
        // once for the decorator's native-hybrid discovery (same configuration).
        Assert.True(factory.CreateCalls >= 1);
        Assert.NotNull(factory.LastConfig);
        Assert.Equal("chromadb", factory.LastConfig!.Type);
        Assert.Equal("http://chroma:8000", factory.LastConfig.ConnectionString);
        Assert.Equal("secret", Assert.Contains("ApiKey", factory.LastConfig.Options!));
    }

    [Fact]
    public void AddOrkeonRag_UnknownProvider_FailsLoudly_WithTheAliasList()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Rag:Provider"] = "mongodb",
            })
            .Build();

        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory>(
            new FakeMemoryProviderFactory());
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRag(configuration);

        using var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IDocumentStore>());

        Assert.Contains("mongodb", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Rag:Provider", ex.Message, StringComparison.Ordinal);
        Assert.Contains("chromadb", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pinecone", ex.Message, StringComparison.Ordinal);
        Assert.Contains("lancedb", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sqlite", ex.Message, StringComparison.Ordinal);
        Assert.Contains("inmemory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOrkeonRag_NoProviderOption_UsesTheAmbientMemoryProvider()
    {
        var factory = new FakeMemoryProviderFactory();

        using var chat = new FakeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService().AddMount("/kb"));
        services.AddSingleton<Orkeon.Domain.Memory.IMemoryProvider>(
            new Stores.Doubles.FakeMemoryProvider());
        services.AddSingleton<Orkeon.Application.Interfaces.Ports.IMemoryProviderFactory>(factory);
        services.AddSingleton<IEmbeddingProvider>(new FakeEmbeddingProvider());
        services.AddSingleton<IChatClient>(chat);

        services.AddOrkeonRag(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        var store = Assert.IsType<Orkeon.Rag.Retrieval.HybridSearchDocumentStore>(
            provider.GetRequiredService<IDocumentStore>());
        Assert.IsType<Orkeon.Rag.Stores.MemoryProviderDocumentStore>(store.Inner);
        Assert.Equal(0, factory.CreateCalls);
    }

    private sealed class HostRagPipeline : IRagPipeline
    {
        public Task<Orkeon.Rag.Abstractions.Models.RagAnswer> QueryAsync(
            Orkeon.Rag.Abstractions.Models.RagQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Orkeon.Rag.Abstractions.Models.RagAnswer { Text = "host" });
    }
}
