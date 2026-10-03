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
/// endpoints. A type it does not serve, and LanceDB without an endpoint, are refused where they are
/// reached (GAP-40): they used to run on the volatile in-memory provider, with a warning, and a crew
/// that was meant to remember forgot at the end of the run. A host's start refuses them before, for
/// <c>Memory:Provider</c> and <c>Orkeon:Rag:Provider</c>.
/// </para>
/// </remarks>
public sealed class MemoryProviderFactory : IMemoryProviderFactory, IDisposable
{
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
    /// <param name="loggerFactory">Optional logger factory, for the providers.</param>
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
    public IReadOnlyList<string> SupportedTypes => MemoryProviderTypes.Supported;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <paramref name="providerType"/> names no type the factory serves, or <c>lancedb</c> without
    /// <c>Orkeon:LanceDb:Endpoint</c> — the message names the type and the known ones.
    /// </exception>
    public IMemoryProvider GetProvider(string providerType)
    {
        var canonical = MemoryProviderTypes.Canonical(providerType)
            ?? throw new InvalidOperationException(MemoryProviderTypes.UnknownMessage("The memory provider type", (providerType ?? string.Empty).Trim()));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetOrCreateLocked(canonical);
        }
    }

    private IMemoryProvider GetOrCreateLocked(string canonicalType)
    {
        if (_providers.TryGetValue(canonicalType, out var existing))
            return existing;

        IMemoryProvider created = canonicalType switch
        {
            MemoryProviderTypes.Redis => new RedisMemoryProvider(
                Options.Create(_settings.Redis), _loggerFactory?.CreateLogger<RedisMemoryProvider>()),
            MemoryProviderTypes.Sqlite => new SqliteMemoryProvider(
                Options.Create(_settings.Sqlite), _fileSystem, _loggerFactory?.CreateLogger<SqliteMemoryProvider>()),
            MemoryProviderTypes.ChromaDb => new ChromaDbMemoryProvider(
                _httpClientFactory.CreateClient(nameof(ChromaDbMemoryProvider)),
                Options.Create(_settings.ChromaDb),
                _loggerFactory?.CreateLogger<ChromaDbMemoryProvider>()),
            MemoryProviderTypes.Pinecone => new PineconeMemoryProvider(
                _httpClientFactory.CreateClient(nameof(PineconeMemoryProvider)),
                Options.Create(_settings.Pinecone),
                _loggerFactory?.CreateLogger<PineconeMemoryProvider>()),
            MemoryProviderTypes.LanceDb => CreateLanceDb(),
            _ => new InMemoryProvider(_loggerFactory?.CreateLogger<InMemoryProvider>()),
        };

        _providers[canonicalType] = created;
        return created;
    }

    private LanceDbMemoryProvider CreateLanceDb()
    {
        if (string.IsNullOrWhiteSpace(_settings.LanceDb.Endpoint))
        {
            throw new InvalidOperationException(
                $"The memory provider type 'lancedb' needs {LanceDbOptions.SectionName}:Endpoint, the LanceDB " +
                "Cloud/Enterprise REST endpoint (with its ApiKey): set it, or name another provider.");
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
}
