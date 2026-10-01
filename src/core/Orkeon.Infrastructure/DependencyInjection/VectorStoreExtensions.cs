using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Migration;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Sqlite;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering memory providers (Redis, ChromaDB, LanceDB, Pinecone) and
/// migration services.
/// </summary>
/// <remarks>
/// Every provider is the <see cref="IMemoryProviderFactory"/>'s shared instance for its type,
/// connected from the host's section for that provider (GAP-08). Resolving the concrete class,
/// the application-wide <see cref="IMemoryProvider"/>, a crew's <c>memoryProvider:</c> or the RAG
/// store of the same type yields that one instance — one connection, owned by the factory.
/// </remarks>
public static class VectorStoreExtensions
{
    /// <summary>
    /// Configuration key choosing the TYPE of the application-wide <see cref="IMemoryProvider"/>
    /// (<c>inmemory</c>, <c>redis</c>, <c>sqlite</c>, <c>chromadb</c>, <c>pinecone</c>, <c>lancedb</c>;
    /// unset → in-memory). The connection comes from that provider's own section.
    /// </summary>
    public const string MemoryProviderKey = "Memory:Provider";

    /// <summary>
    /// Registers the <see cref="IMemoryProviderFactory"/> and what it needs (the host's
    /// per-provider sections as <see cref="MemoryProviderSettings"/>, <see cref="IHttpClientFactory"/>).
    /// Idempotent; <c>AddOrkeonInfrastructure()</c> calls it. A host-registered factory wins.
    /// </summary>
    public static IServiceCollection AddOrkeonMemoryProviderFactory(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.TryAddSingleton(sp => new MemoryProviderSettings
        {
            Redis = sp.GetRequiredService<IOptions<RedisMemoryOptions>>().Value,
            Sqlite = sp.GetRequiredService<IOptions<SqliteMemoryOptions>>().Value,
            ChromaDb = sp.GetRequiredService<IOptions<ChromaDbOptions>>().Value,
            Pinecone = sp.GetRequiredService<IOptions<PineconeOptions>>().Value,
            LanceDb = sp.GetRequiredService<IOptions<LanceDbOptions>>().Value,
        });
        services.TryAddSingleton<IMemoryProviderFactory, MemoryProviderFactory>();
        return services;
    }

    /// <summary>
    /// Binds the <c>Orkeon:ChromaDb</c> section and exposes the shared
    /// <see cref="ChromaDbMemoryProvider"/> as its concrete class. Does not change the
    /// application-wide <see cref="IMemoryProvider"/> (select it with <c>Memory:Provider = chromadb</c>).
    /// </summary>
    public static IServiceCollection AddOrkeonChromaDb(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<ChromaDbOptions>()
            .Bind(configuration.GetSection(ChromaDbOptions.SectionName));
        services.AddOrkeonMemoryProviderFactory();
        services.TryAddSingleton(sp => SharedProvider<ChromaDbMemoryProvider>(sp, "chromadb"));

        return services;
    }

    /// <summary>
    /// Binds the <c>Orkeon:LanceDb</c> section (<c>Endpoint</c>, <c>ApiKey</c>, <c>Database</c>,
    /// <c>TableName</c>, …), exposes the shared <see cref="LanceDbMemoryProvider"/> as its concrete
    /// class and registers <see cref="LanceDbMigrationService"/>. Resolving the provider without an
    /// <c>Endpoint</c> throws. Does not change the application-wide <see cref="IMemoryProvider"/>.
    /// </summary>
    public static IServiceCollection AddOrkeonLanceDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<LanceDbOptions>()
            .Bind(configuration.GetSection(LanceDbOptions.SectionName));
        services.AddOrkeonMemoryProviderFactory();
        services.TryAddSingleton(sp => SharedProvider<LanceDbMemoryProvider>(sp, "lancedb"));
        services.TryAddSingleton<LanceDbMigrationService>();

        return services;
    }

    /// <summary>
    /// Binds the <c>Orkeon:Pinecone</c> section (<c>ApiKey</c>, <c>IndexName</c>, <c>Host</c>,
    /// <c>Namespace</c>) and exposes the shared <see cref="PineconeMemoryProvider"/> as its concrete
    /// class. Does not change the application-wide <see cref="IMemoryProvider"/>.
    /// </summary>
    public static IServiceCollection AddOrkeonPinecone(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<PineconeOptions>()
            .Bind(configuration.GetSection(PineconeOptions.SectionName));
        services.AddOrkeonMemoryProviderFactory();
        services.TryAddSingleton(sp => SharedProvider<PineconeMemoryProvider>(sp, "pinecone"));

        return services;
    }

    /// <summary>
    /// Binds the <c>Orkeon:Redis</c> section (<c>ConnectionString</c>, <c>KeyPrefix</c>) and makes
    /// the shared <see cref="RedisMemoryProvider"/> the application-wide <see cref="IMemoryProvider"/>
    /// — the same as <c>Memory:Provider = redis</c>. The provider connects on first use; there is no
    /// initialization step.
    /// </summary>
    public static IServiceCollection AddOrkeonRedisMemory(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<RedisMemoryOptions>()
            .Bind(configuration.GetSection(RedisMemoryOptions.SectionName));
        services.AddOrkeonMemoryProviderFactory();
        services.TryAddSingleton(sp => SharedProvider<RedisMemoryProvider>(sp, "redis"));

        // Override the in-memory baseline so IMemoryProvider resolves to Redis.
        services.RemoveAll<IMemoryProvider>();
        services.AddSingleton<IMemoryProvider>(sp => sp.GetRequiredService<RedisMemoryProvider>());

        return services;
    }

    /// <summary>
    /// Registers the memory migration service.
    /// </summary>
    public static IServiceCollection AddOrkeonMemoryMigration(this IServiceCollection services)
    {
        services.AddSingleton<MemoryMigrationService>();
        return services;
    }

    private static TProvider SharedProvider<TProvider>(IServiceProvider serviceProvider, string providerType)
        where TProvider : class, IMemoryProvider
    {
        var provider = serviceProvider.GetRequiredService<IMemoryProviderFactory>().GetProvider(providerType);
        return provider as TProvider
            ?? throw new InvalidOperationException(
                $"The memory provider factory returned {provider.GetType().Name} for type '{providerType}', " +
                $"not {typeof(TProvider).Name}: check that provider's configuration section " +
                "(a LanceDB provider needs 'Orkeon:LanceDb:Endpoint').");
    }
}
