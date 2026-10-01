using Orkeon.Application.Interfaces.Ports;
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
/// Hands out one shared memory provider per type (inmemory, redis, sqlite, chromadb, pinecone,
/// lancedb), connected from the host's section for that provider (<see cref="MemoryProviderSettings"/>).
/// The type is the only thing a caller chooses (GAP-08).
/// </summary>
/// <remarks>
/// <para>
/// The factory owns the instances it creates and disposes them with itself — callers never do.
/// Creation is lazy (first <see cref="GetProvider"/> for a type) and never connects: each provider
/// opens its connection on its own first use. A creation that throws (e.g. an SQLite
/// <c>Data Source</c> outside any writable mount) is not cached.
/// </para>
/// <para>
/// HTTP-backed providers (ChromaDB, Pinecone, LanceDB) get a named client from
/// <see cref="IHttpClientFactory"/>, whose handler rotation re-observes DNS changes on their
/// endpoints. An unrecognized type, and LanceDB without an endpoint, resolve to the shared
/// in-memory provider with an explicit warning — never a silent fallback.
/// </para>
/// </remarks>
public sealed partial class MemoryProviderFactory : IMemoryProviderFactory, IDisposable
{
    private const string InMemoryType = "inmemory";
    private const string RedisType = "redis";
    private const string SqliteType = "sqlite";
    private const string ChromaDbType = "chromadb";
    private const string PineconeType = "pinecone";
    private const string LanceDbType = "lancedb";

    private static readonly string[] s_supportedTypes =
        [InMemoryType, "in-memory", RedisType, SqliteType, ChromaDbType, "chroma", PineconeType, LanceDbType, "lance"];

    private readonly IFileSystemService _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MemoryProviderSettings _settings;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Dictionary<string, IMemoryProvider> _providers = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryProviderFactory"/>.
    /// </summary>
    /// <param name="fileSystem">Virtual file system governing the SQLite database file path (VFS-70 / 2F-A). Required.</param>
    /// <param name="httpClientFactory">Source of the HTTP clients of the ChromaDB, Pinecone and LanceDB providers.</param>
    /// <param name="settings">The host's per-provider sections; <see langword="null"/> runs every provider on its defaults.</param>
    /// <param name="loggerFactory">Optional logger factory, for the providers and the fallback warnings.</param>
    public MemoryProviderFactory(
        IFileSystemService fileSystem,
        IHttpClientFactory httpClientFactory,
        MemoryProviderSettings? settings = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
        _settings = settings ?? new MemoryProviderSettings();
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedTypes => s_supportedTypes;

    /// <inheritdoc />
    public IMemoryProvider GetProvider(string providerType)
    {
#pragma warning disable CA1308 // lowercase is the required switch-key form, not a comparison normalization
        var requested = (providerType ?? string.Empty).Trim().ToLowerInvariant();
#pragma warning restore CA1308
        var canonical = Canonicalize(requested);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (canonical is null)
            {
                LogUnknownProviderType(Logger, string.IsNullOrEmpty(providerType) ? requested : providerType);
                return GetOrCreateLocked(InMemoryType);
            }

            return GetOrCreateLocked(canonical);
        }
    }

    private static string? Canonicalize(string type) => type switch
    {
        InMemoryType or "in-memory" or "" => InMemoryType,
        RedisType => RedisType,
        SqliteType => SqliteType,
        ChromaDbType or "chroma" => ChromaDbType,
        PineconeType => PineconeType,
        LanceDbType or "lance" => LanceDbType,
        _ => null,
    };

    private ILogger Logger =>
        (ILogger?)_loggerFactory?.CreateLogger<MemoryProviderFactory>()
        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private IMemoryProvider GetOrCreateLocked(string canonicalType)
    {
        if (_providers.TryGetValue(canonicalType, out var existing))
            return existing;

        var created = canonicalType switch
        {
            RedisType => new RedisMemoryProvider(
                Options.Create(_settings.Redis), _loggerFactory?.CreateLogger<RedisMemoryProvider>()),
            SqliteType => new SqliteMemoryProvider(
                Options.Create(_settings.Sqlite), _fileSystem, _loggerFactory?.CreateLogger<SqliteMemoryProvider>()),
            ChromaDbType => new ChromaDbMemoryProvider(
                _httpClientFactory.CreateClient(nameof(ChromaDbMemoryProvider)),
                Options.Create(_settings.ChromaDb),
                _loggerFactory?.CreateLogger<ChromaDbMemoryProvider>()),
            PineconeType => new PineconeMemoryProvider(
                _httpClientFactory.CreateClient(nameof(PineconeMemoryProvider)),
                Options.Create(_settings.Pinecone),
                _loggerFactory?.CreateLogger<PineconeMemoryProvider>()),
            LanceDbType => CreateLanceDbLocked(),
            _ => new InMemoryProvider(_loggerFactory?.CreateLogger<InMemoryProvider>()),
        };

        _providers[canonicalType] = created;
        return created;
    }

    private IMemoryProvider CreateLanceDbLocked()
    {
        if (string.IsNullOrWhiteSpace(_settings.LanceDb.Endpoint))
        {
            LogLanceDbMissingEndpoint(Logger);
            return GetOrCreateLocked(InMemoryType);
        }

        return new LanceDbMemoryProvider(
            _httpClientFactory.CreateClient(nameof(LanceDbMemoryProvider)),
            Options.Create(_settings.LanceDb),
            _loggerFactory?.CreateLogger<LanceDbMemoryProvider>());
    }

    /// <summary>Disposes every provider the factory created, once each.</summary>
    public void Dispose()
    {
        List<IMemoryProvider> owned;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            owned = _providers.Values.Distinct<IMemoryProvider>(ReferenceEqualityComparer.Instance).ToList();
            _providers.Clear();
        }

        foreach (var provider in owned)
            (provider as IDisposable)?.Dispose();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Memory provider type 'lancedb' needs the LanceDB Cloud/Enterprise REST endpoint in '" +
            LanceDbOptions.SectionName + ":Endpoint' (plus an ApiKey). Falling back to the in-memory provider (volatile).")]
    static partial void LogLanceDbMissingEndpoint(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Memory provider type '{ProviderType}' is not recognized; falling back to the in-memory provider (volatile). " +
            "Supported types: inmemory, redis, sqlite, chromadb, pinecone, lancedb.")]
    static partial void LogUnknownProviderType(ILogger logger, string providerType);
}
