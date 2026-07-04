using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Migration;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering vector store providers (ChromaDB, LanceDB, Pinecone) and migration services.
/// </summary>
public static class VectorStoreExtensions
{
    /// <summary>
    /// Registers the ChromaDB memory provider and its options.
    /// Reads configuration from the "Orkeon:ChromaDb" section.
    /// </summary>
    public static IServiceCollection AddOrkeonChromaDb(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<ChromaDbOptions>()
            .Bind(configuration.GetSection("Orkeon:ChromaDb"));

        services.AddSingleton<ChromaDbMemoryProvider>();

        return services;
    }

    /// <summary>
    /// Registers the LanceDB memory provider (remote LanceDB Cloud/Enterprise server,
    /// Lance REST Namespace protocol) and its options.
    /// Reads configuration from the "Orkeon:LanceDb" section
    /// (<c>Endpoint</c>, <c>ApiKey</c>, <c>Database</c>, <c>TableName</c>, …).
    /// </summary>
    public static IServiceCollection AddOrkeonLanceDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<LanceDbOptions>()
            .Bind(configuration.GetSection("Orkeon:LanceDb"));

        services.AddHttpClient();
        services.AddSingleton<LanceDbMemoryProvider>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LanceDbMemoryProvider));
            var options = sp.GetRequiredService<IOptions<LanceDbOptions>>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<LanceDbMemoryProvider>>();
            return new LanceDbMemoryProvider(httpClient, options, logger);
        });
        services.AddSingleton<LanceDbMigrationService>();

        return services;
    }

    /// <summary>
    /// Registers the Pinecone memory provider and its options.
    /// Reads configuration from the "Orkeon:Pinecone" section.
    /// </summary>
    public static IServiceCollection AddOrkeonPinecone(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<PineconeOptions>()
            .Bind(configuration.GetSection("Orkeon:Pinecone"));

        services.AddSingleton<PineconeMemoryProvider>();

        return services;
    }

    /// <summary>
    /// Registers the Redis memory provider and resolves <see cref="IMemoryProvider"/> to it.
    /// This replaces the default in-memory baseline wiring so that a Redis-configured deployment
    /// gets distributed, persistent memory instead of a volatile in-memory fallback.
    /// The Redis connection string is supplied at initialization time via the provider's
    /// <c>InitializeAsync</c>; binding here only registers the type.
    /// </summary>
    public static IServiceCollection AddOrkeonRedisMemory(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<RedisMemoryProvider>(sp =>
        {
            var logger = sp.GetService<ILogger<RedisMemoryProvider>>();
            return new RedisMemoryProvider(logger);
        });

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
}
