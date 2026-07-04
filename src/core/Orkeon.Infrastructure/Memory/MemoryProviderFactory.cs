using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Memory;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Memory.LanceDb;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Memory.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Factory for creating memory providers based on configuration.
/// Resolves the concrete provider from <see cref="MemoryProviderConfigDto.Type"/>
/// (inmemory, redis, sqlite, chromadb, pinecone, lancedb). An unrecognized type is logged
/// explicitly before falling back to the in-memory provider — never a silent fallback.
/// </summary>
/// <remarks>
/// The LanceDB provider targets a remote LanceDB Cloud/Enterprise server: the
/// connection string supplies the REST endpoint, and <c>Options</c> may carry
/// <c>ApiKey</c>, <c>TableName</c> and <c>Database</c>. Without an endpoint the
/// factory logs an explicit warning (pointing at <c>AddOrkeonLanceDb</c>) rather
/// than failing silently.
/// </remarks>
public sealed partial class MemoryProviderFactory : IMemoryProviderFactory
{
    private readonly IFileSystemService _fileSystem;

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryProviderFactory"/>.
    /// </summary>
    /// <param name="fileSystem">Virtual file system used to govern the SQLite database file path (VFS-70 / 2F-A). Required.</param>
    public MemoryProviderFactory(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public IMemoryProvider Create(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);

#pragma warning disable CA1308 // lowercase is the required switch-key form, not a comparison normalization
        var type = (config.Type ?? string.Empty).Trim().ToLowerInvariant();
#pragma warning restore CA1308

        return type switch
        {
            "inmemory" or "in-memory" or "" => CreateInMemory(loggerFactory),
            "redis" => new RedisMemoryProvider(loggerFactory?.CreateLogger<RedisMemoryProvider>()),
            "sqlite" => CreateSqlite(config, _fileSystem, loggerFactory),
            "chromadb" or "chroma" => CreateChromaDb(config, loggerFactory),
            "pinecone" => CreatePinecone(config, loggerFactory),
            "lancedb" or "lance" => CreateLanceDb(config, loggerFactory),
            _ => CreateUnknownFallback(config, type, loggerFactory),
        };
    }

    private static InMemoryProvider CreateInMemory(ILoggerFactory? loggerFactory) =>
        new(loggerFactory?.CreateLogger<InMemoryProvider>());

    private static SqliteMemoryProvider CreateSqlite(MemoryProviderConfigDto config, IFileSystemService fileSystem, ILoggerFactory? loggerFactory)
    {
        var options = new SqliteMemoryOptions();
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            options.ConnectionString = config.ConnectionString;
        }

        if (config.Options is not null
            && config.Options.TryGetValue("TableName", out var table)
            && table is string tableStr
            && !string.IsNullOrWhiteSpace(tableStr))
        {
            options.TableName = tableStr;
        }

        return new SqliteMemoryProvider(
            Options.Create(options),
            fileSystem,
            loggerFactory?.CreateLogger<SqliteMemoryProvider>());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the created HttpClient transfers to the returned ChromaDbMemoryProvider, which disposes it in its Dispose(bool).")]
    private static ChromaDbMemoryProvider CreateChromaDb(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory)
    {
        var options = new ChromaDbOptions();
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            options.BaseUrl = new Uri(config.ConnectionString);
        }

        // PooledConnectionLifetime recycles pooled connections so this long-lived client
        // re-observes DNS changes on the ChromaDB endpoint (ANT-013). Ownership transfers
        // to the provider, which disposes it.
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        });
        return new ChromaDbMemoryProvider(
            httpClient,
            Options.Create(options),
            loggerFactory?.CreateLogger<ChromaDbMemoryProvider>());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the created HttpClient transfers to the returned PineconeMemoryProvider, which disposes it in its Dispose(bool).")]
    private static PineconeMemoryProvider CreatePinecone(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory)
    {
        var options = new PineconeOptions();
        if (config.Options is not null)
        {
            if (config.Options.TryGetValue("ApiKey", out var apiKey) && apiKey is string apiKeyStr)
            {
                options.ApiKey = apiKeyStr;
            }

            if (config.Options.TryGetValue("Environment", out var env) && env is string envStr)
            {
                options.Environment = envStr;
            }

            if (config.Options.TryGetValue("IndexName", out var index) && index is string indexStr)
            {
                options.IndexName = indexStr;
            }

            if (config.Options.TryGetValue("Namespace", out var ns) && ns is string nsStr)
            {
                options.Namespace = nsStr;
            }
        }

        // PooledConnectionLifetime recycles pooled connections so this long-lived client
        // re-observes DNS changes on rotating Pinecone cloud endpoints (ANT-013).
        // Ownership transfers to the provider, which disposes it.
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        });
        return new PineconeMemoryProvider(
            httpClient,
            Options.Create(options),
            loggerFactory?.CreateLogger<PineconeMemoryProvider>());
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the created HttpClient transfers to the returned LanceDbMemoryProvider, which disposes it in its Dispose(bool).")]
    private static IMemoryProvider CreateLanceDb(MemoryProviderConfigDto config, ILoggerFactory? loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            return CreateLanceDbMissingEndpointFallback(config, loggerFactory);
        }

        var options = new LanceDbOptions { Endpoint = config.ConnectionString };
        if (config.Options is not null)
        {
            if (config.Options.TryGetValue("ApiKey", out var apiKey) && apiKey is string apiKeyStr)
            {
                options.ApiKey = apiKeyStr;
            }

            if (config.Options.TryGetValue("TableName", out var table) && table is string tableStr)
            {
                options.TableName = tableStr;
            }

            if (config.Options.TryGetValue("Database", out var database) && database is string databaseStr)
            {
                options.Database = databaseStr;
            }
        }

        // PooledConnectionLifetime recycles pooled connections so this long-lived client
        // re-observes DNS changes on rotating LanceDB Cloud/Enterprise endpoints (ANT-013).
        // Ownership transfers to the provider, which disposes it.
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        });
        return new LanceDbMemoryProvider(
            httpClient,
            Options.Create(options),
            loggerFactory?.CreateLogger<LanceDbMemoryProvider>());
    }

    private static InMemoryProvider CreateLanceDbMissingEndpointFallback(
        MemoryProviderConfigDto config,
        ILoggerFactory? loggerFactory)
    {
        var logger = loggerFactory?.CreateLogger<MemoryProviderFactory>();
        if (logger is not null)
            LogLanceDbMissingEndpoint(logger, config.Type);

        return CreateInMemory(loggerFactory);
    }

    private static InMemoryProvider CreateUnknownFallback(
        MemoryProviderConfigDto config,
        string normalizedType,
        ILoggerFactory? loggerFactory)
    {
        var logger = loggerFactory?.CreateLogger<MemoryProviderFactory>();
        if (logger is not null)
            LogUnknownProviderType(logger, string.IsNullOrEmpty(config.Type) ? normalizedType : config.Type);

        return CreateInMemory(loggerFactory);
    }

    /// <inheritdoc />
    public async Task<IMemoryProvider> CreateAndInitializeAsync(
        MemoryProviderConfigDto config,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        var provider = Create(config, loggerFactory);
        return await Task.FromResult(provider).ConfigureAwait(false);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Memory provider type '{ProviderType}' requires the LanceDB Cloud/Enterprise REST endpoint as connection " +
            "string (plus an ApiKey option), or a bound configuration via AddOrkeonLanceDb in the DI container. " +
            "Falling back to the in-memory provider (volatile).")]
    static partial void LogLanceDbMissingEndpoint(ILogger logger, string providerType);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Memory provider type '{ProviderType}' is not recognized; falling back to the in-memory provider (volatile). " +
            "Supported types: inmemory, redis, sqlite, chromadb, pinecone, lancedb.")]
    static partial void LogUnknownProviderType(ILogger logger, string providerType);
}
