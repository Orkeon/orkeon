using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>Configuration for memory settings.</summary>
public sealed record MemoryConfig
{
    /// <summary>Gets a value indicating whether memory is enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets the type of memory storage.</summary>
    public MemoryStorageType Type { get; init; } = MemoryStorageType.InMemory;
    /// <summary>Gets the provider name.</summary>
    public string Provider { get; init; } = MemoryDefaults.DefaultProvider;

    /// <summary>
    /// Connection string for the memory provider (e.g. Redis connection string).
    /// Typed alternative to <c>Settings["connectionString"]</c>.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Key prefix used by the memory provider to namespace entries.
    /// Typed alternative to <c>Settings["keyPrefix"]</c>.
    /// </summary>
    public string? KeyPrefix { get; init; }

    /// <summary>
    /// Additional provider-specific settings.
    /// Prefer the typed properties (<see cref="ConnectionString"/>, <see cref="KeyPrefix"/>)
    /// for well-known keys. Use this dictionary only for provider-specific extensions.
    /// </summary>
    public Dictionary<string, object> Settings { get; init; } = [];
    /// <summary>Gets the retention period for stored memories, or null for no limit.</summary>
    public TimeSpan? RetentionPeriod { get; init; }
    /// <summary>Gets the maximum number of items to store.</summary>
    public int MaxItems { get; init; } = MemoryDefaults.LongTermCapacity;
    /// <summary>Gets a value indicating whether to persist memories to disk.</summary>
    public bool PersistToDisk { get; init; }
    /// <summary>Gets the storage path for disk persistence, or null if not persisting.</summary>
    public string? StoragePath { get; init; }
}

/// <summary>Types of memory storage providers.</summary>
public enum MemoryStorageType
{
    /// <summary>In-process in-memory storage.</summary>
    InMemory,
    /// <summary>Redis distributed memory storage.</summary>
    Redis,
    /// <summary>SQLite file-based storage.</summary>
    SQLite,
    /// <summary>ChromaDB vector storage.</summary>
    ChromaDB,
    /// <summary>Pinecone cloud vector storage.</summary>
    Pinecone,
    /// <summary>Custom memory provider.</summary>
    Custom
}

/// <summary>Log levels for telemetry.</summary>
public enum TelemetryLogLevel
{
    /// <summary>Trace level logging.</summary>
    Trace,
    /// <summary>Debug level logging.</summary>
    Debug,
    /// <summary>Information level logging.</summary>
    Information,
    /// <summary>Warning level logging.</summary>
    Warning,
    /// <summary>Error level logging.</summary>
    Error,
    /// <summary>Critical level logging.</summary>
    Critical
}

/// <summary>Configuration for telemetry.</summary>
public sealed record TelemetryConfig
{
    /// <summary>Gets a value indicating whether telemetry is enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets the log level for telemetry output.</summary>
    public TelemetryLogLevel LogLevel { get; init; } = TelemetryLogLevel.Information;
    /// <summary>Gets the list of exporters to use.</summary>
    public IReadOnlyList<string> Exporters { get; init; } = ["console"];
    /// <summary>Gets settings for each exporter.</summary>
    public Dictionary<string, object> ExporterSettings { get; init; } = [];
    /// <summary>Gets a value indicating whether to include detailed error information.</summary>
    public bool IncludeDetailedErrors { get; init; }
    /// <summary>Gets a value indicating whether to track performance metrics.</summary>
    public bool TrackPerformanceMetrics { get; init; } = true;
}
