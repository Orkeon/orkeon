using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Sqlite;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using StackExchange.Redis;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// <see cref="MemoryProviderFactory"/> resolves the provider from its type alone and connects it
/// from the host's section for that provider (GAP-08): one shared instance per type, owned by the
/// factory, and an unrecognized type is a warning, never a silent fallback.
/// </summary>
public sealed class MemoryProviderFactoryTests : IDisposable
{
    /// <summary>A port nothing listens on, refused at once: a connection error, fast.</summary>
    private const string UnreachableRedis = "127.0.0.1:1,abortConnect=true,connectTimeout=100";

    private readonly MockHttpClientFactory _httpClientFactory = new();

    public void Dispose() => _httpClientFactory.Dispose();

    private MemoryProviderFactory CreateFactory(MemoryProviderSettings? settings = null, ILoggerFactory? loggerFactory = null) =>
        new(new FakeFileSystemService(), _httpClientFactory, settings, loggerFactory);

    private static MemoryItem Item(string content = "insight") =>
        MemoryItem.Create(content: content, embedding: [0.1f, 0.2f, 0.3f], importance: 0.9f, source: "test");

    [Theory]
    [InlineData("inmemory", typeof(InMemoryProvider))]
    [InlineData("InMemory", typeof(InMemoryProvider))]
    [InlineData("", typeof(InMemoryProvider))]
    [InlineData("redis", typeof(RedisMemoryProvider))]
    [InlineData("Redis", typeof(RedisMemoryProvider))]
    [InlineData("sqlite", typeof(SqliteMemoryProvider))]
    [InlineData("SQLite", typeof(SqliteMemoryProvider))]
    [InlineData("chromadb", typeof(ChromaDbMemoryProvider))]
    [InlineData("chroma", typeof(ChromaDbMemoryProvider))]
    [InlineData("pinecone", typeof(PineconeMemoryProvider))]
    public void GetProvider_ShouldResolveProviderFromType(string type, Type expectedProviderType)
    {
        using var factory = CreateFactory();

        var provider = factory.GetProvider(type);

        Assert.IsType(expectedProviderType, provider);
    }

    [Fact]
    public void GetProvider_SameType_ShouldReturnOneSharedInstance_WhateverTheAlias()
    {
        using var factory = CreateFactory();

        Assert.Same(factory.GetProvider("redis"), factory.GetProvider("Redis"));
        Assert.Same(factory.GetProvider("chromadb"), factory.GetProvider("chroma"));
        Assert.Same(factory.GetProvider("inmemory"), factory.GetProvider("in-memory"));
        Assert.NotSame(factory.GetProvider("inmemory"), factory.GetProvider("sqlite"));
    }

    [Fact]
    public void SupportedTypes_ShouldListEveryAliasTheFactoryResolves()
    {
        using var factory = CreateFactory();

        Assert.Equal(
            ["inmemory", "in-memory", "redis", "sqlite", "chromadb", "chroma", "pinecone", "lancedb", "lance"],
            factory.SupportedTypes);
    }

    /// <summary>
    /// GAP-40, decision 6 — a type the factory does not serve is refused where it is reached, with the
    /// types it serves: it used to run on the volatile in-memory provider, with a warning, and a crew
    /// meant to remember forgot at the end of the run.
    /// </summary>
    [Fact]
    public void GetProvider_UnknownType_ShouldBeRefused_NamingTheSupportedTypes_NotFallBack()
    {
        using var factory = CreateFactory();

        var error = Assert.Throws<InvalidOperationException>(() => factory.GetProvider("cosmosdb"));

        Assert.Contains("cosmosdb", error.Message, StringComparison.Ordinal);
        Assert.Contains("sqlite", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProvider_LanceDb_WithEndpoint_ShouldResolveLanceDbProvider()
    {
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            LanceDb = new LanceDbOptions
            {
                Endpoint = "https://orkeon-tests.us-east-1.api.lancedb.com",
                ApiKey = "factory-test-key",
                TableName = "factory_test",
            },
        });

        var lanceDb = Assert.IsType<LanceDbMemoryProvider>(factory.GetProvider("lancedb"));
        Assert.Equal("LanceDB", lanceDb.Name);
        Assert.Equal(nameof(LanceDbMemoryProvider), _httpClientFactory.LastClientName);
    }

    [Fact]
    public void GetProvider_LanceDb_WithoutEndpoint_ShouldBeRefused_NamingTheEndpoint_NotFallBack()
    {
        using var factory = CreateFactory();

        var error = Assert.Throws<InvalidOperationException>(() => factory.GetProvider("lancedb"));

        Assert.Contains("Orkeon:LanceDb:Endpoint", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChromaDb_ShouldCallTheBaseUrlOfItsSection()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(ChromaDbMemoryProvider));
        handler.SetResponseFactory(_ => ChromaCollectionResponse());
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            ChromaDb = new ChromaDbOptions { BaseUrl = new Uri("http://chroma.internal:9123") },
        });

        await factory.GetProvider("chromadb").StoreAsync("k1", Item(), TestContext.Current.CancellationToken);

        Assert.NotEmpty(handler.AllRequests);
        Assert.All(handler.AllRequests, r => Assert.Equal("chroma.internal", r.RequestUri!.Host));
        Assert.All(handler.AllRequests, r => Assert.Equal(9123, r.RequestUri!.Port));
    }

    [Fact]
    public async Task Pinecone_WithHost_ShouldSendTheFirstRequestToThatHost()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(PineconeMemoryProvider));
        handler.SetResponse(HttpStatusCode.OK, "{\"upsertedCount\":1}", "application/json");
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            Pinecone = new PineconeOptions
            {
                ApiKey = "pc-key",
                IndexName = "orkeon-memories",
                Host = "orkeon-memories-abc1234.svc.aped-4627-b74a.pinecone.io",
            },
        });

        await factory.GetProvider("pinecone").StoreAsync("k1", Item(), TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.AllRequests);
        Assert.Equal(
            "https://orkeon-memories-abc1234.svc.aped-4627-b74a.pinecone.io/vectors/upsert",
            request.RequestUri!.ToString());
        Assert.Equal("pc-key", request.Headers.GetValues("Api-Key").Single());
    }

    [Fact]
    public async Task Pinecone_WithoutHost_ShouldDescribeTheIndexOnce_AndUseTheHostItReturns()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(PineconeMemoryProvider));
        handler.SetResponseFactory(request => request.RequestUri!.Host == "api.pinecone.io"
            ? Json("{\"name\":\"orkeon-memories\",\"host\":\"orkeon-memories-xyz9876.svc.us-east-1-aws.pinecone.io\"}")
            : Json("{\"upsertedCount\":1}"));
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            Pinecone = new PineconeOptions { ApiKey = "pc-key", IndexName = "orkeon-memories" },
        });
        var provider = factory.GetProvider("pinecone");

        await provider.StoreAsync("k1", Item(), TestContext.Current.CancellationToken);
        await provider.StoreAsync("k2", Item(), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "https://api.pinecone.io/indexes/orkeon-memories",
                "https://orkeon-memories-xyz9876.svc.us-east-1-aws.pinecone.io/vectors/upsert",
                "https://orkeon-memories-xyz9876.svc.us-east-1-aws.pinecone.io/vectors/upsert",
            ],
            handler.AllRequests.Select(r => r.RequestUri!.ToString()));
        Assert.Equal(HttpMethod.Get, handler.AllRequests[0].Method);
    }

    [Fact]
    public async Task Pinecone_WithoutHost_WhenDescribeFails_ShouldSayWhichKeysToCheck()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(PineconeMemoryProvider));
        handler.SetResponse(HttpStatusCode.NotFound, "{\"error\":\"not found\"}", "application/json");
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            Pinecone = new PineconeOptions { ApiKey = "pc-key", IndexName = "missing-index" },
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.GetProvider("pinecone").StoreAsync("k1", Item(), TestContext.Current.CancellationToken));

        Assert.Contains("missing-index", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Pinecone", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redis_FromTheFactory_ShouldConnectOnFirstUse_NotDemandInitializeAsync()
    {
        using var factory = CreateFactory(new MemoryProviderSettings
        {
            Redis = new RedisMemoryOptions { ConnectionString = UnreachableRedis },
        });
        var provider = factory.GetProvider("redis");

        // No InitializeAsync anywhere: the first call tries to connect. Without a server the
        // failure is the connection's, not the "not initialized" guard it used to be.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => provider.StoreAsync("k1", Item(), TestContext.Current.CancellationToken));

        Assert.IsType<RedisConnectionException>(ex);
        Assert.DoesNotContain("InitializeAsync", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddOrkeonRedisMemory_ShouldResolveIMemoryProvider_ToARedisProviderConnectedFromItsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:Redis:ConnectionString"] = UnreachableRedis,
            })
            .Build();
        var services = NewHostServices(configuration);

        services.AddOrkeonRedisMemory(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var memoryProvider = serviceProvider.GetRequiredService<IMemoryProvider>();

        Assert.IsType<RedisMemoryProvider>(memoryProvider);
        Assert.Same(memoryProvider, serviceProvider.GetRequiredService<IMemoryProviderFactory>().GetProvider("redis"));
        await Assert.ThrowsAsync<RedisConnectionException>(
            () => memoryProvider.StoreAsync("k1", Item(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MemoryProvider_ChromaDb_ShouldReachTheBaseUrlOfTheChromaDbSection()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(ChromaDbMemoryProvider));
        handler.SetResponseFactory(_ => ChromaCollectionResponse());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Memory:Provider"] = "chromadb",
                ["Orkeon:ChromaDb:BaseUrl"] = "http://chroma.internal:9123",
                ["Orkeon:ChromaDb:CollectionName"] = "team_memories",
            })
            .Build();
        var services = NewHostServices(configuration);
        services.AddSingleton<IHttpClientFactory>(_httpClientFactory);
        using var serviceProvider = services.BuildServiceProvider();

        var memoryProvider = serviceProvider.GetRequiredService<IMemoryProvider>();
        await memoryProvider.StoreAsync("k1", Item(), TestContext.Current.CancellationToken);

        Assert.IsType<ChromaDbMemoryProvider>(memoryProvider);
        Assert.Same(memoryProvider, serviceProvider.GetRequiredService<IMemoryProviderFactory>().GetProvider("chroma"));
        Assert.All(handler.AllRequests, r => Assert.Equal("chroma.internal", r.RequestUri!.Host));
        Assert.Contains("team_memories", handler.AllRequests[0].Content is null
            ? string.Empty
            : await handler.AllRequests[0].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MemoryProvider_Pinecone_ShouldTargetTheRealIndexHost()
    {
        using var handler = _httpClientFactory.AddClientWithHandler(nameof(PineconeMemoryProvider));
        handler.SetResponseFactory(request => request.RequestUri!.Host == "api.pinecone.io"
            ? Json("{\"host\":\"team-idx-q1w2e3.svc.eu-west1-gcp.pinecone.io\"}")
            : Json("{\"upsertedCount\":1}"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Memory:Provider"] = "pinecone",
                ["Orkeon:Pinecone:ApiKey"] = "pc-key",
                ["Orkeon:Pinecone:IndexName"] = "team-idx",
            })
            .Build();
        var services = NewHostServices(configuration);
        services.AddSingleton<IHttpClientFactory>(_httpClientFactory);
        using var serviceProvider = services.BuildServiceProvider();

        await serviceProvider.GetRequiredService<IMemoryProvider>()
            .StoreAsync("k1", Item(), TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://team-idx-q1w2e3.svc.eu-west1-gcp.pinecone.io/vectors/upsert",
            handler.AllRequests[^1].RequestUri!.ToString());
    }

    [Fact]
    public async Task Dispose_ShouldDisposeEveryProviderItCreated()
    {
        var factory = CreateFactory();
        var sqlite = (SqliteMemoryProvider)factory.GetProvider("sqlite");

        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.GetProvider("sqlite"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sqlite.CountAsync(TestContext.Current.CancellationToken));
    }

    private static ServiceCollection NewHostServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService());
        services.AddOrkeonInfrastructure();
        return services;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ChromaCollectionResponse() =>
        Json("{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"name\":\"orkeon_memories\"}");
}
