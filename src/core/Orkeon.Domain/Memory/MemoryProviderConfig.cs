using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Configuration for memory providers.
/// </summary>
public record MemoryProviderConfig
{
    /// <summary>Gets the type identifier for this memory provider.</summary>
    public string ProviderType { get; }
    /// <summary>Gets the settings dictionary for this provider.</summary>
    public Dictionary<string, object> Settings { get; }
    /// <summary>Gets the optional retention period for stored items.</summary>
    public TimeSpan? RetentionPeriod { get; }
    /// <summary>Gets the optional maximum number of items to store.</summary>
    public int? MaxItems { get; }
    /// <summary>Gets a value indicating whether persistence is enabled.</summary>
    public bool EnablePersistence { get; }
    /// <summary>Gets the optional connection string for external storage.</summary>
    public string? ConnectionString { get; }

    /// <summary>
    /// Key prefix used by the memory provider to namespace entries.
    /// Typed alternative to <c>Settings["keyPrefix"]</c>.
    /// </summary>
    public string? KeyPrefix { get; }

    /// <summary>Initializes a new instance of <see cref="MemoryProviderConfig"/>.</summary>
    /// <param name="providerType">The provider type identifier.</param>
    /// <param name="settings">Additional settings dictionary.</param>
    /// <param name="retentionPeriod">The retention period for stored items.</param>
    /// <param name="maxItems">Maximum number of items to store.</param>
    /// <param name="enablePersistence">Whether to enable persistence.</param>
    /// <param name="connectionString">The connection string for external storage.</param>
    /// <param name="keyPrefix">Key prefix for namespacing entries.</param>
    public MemoryProviderConfig(
        string providerType,
        Dictionary<string, object>? settings = null,
        TimeSpan? retentionPeriod = null,
        int? maxItems = null,
        bool enablePersistence = false,
        string? connectionString = null,
        string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ProviderType = providerType;
        Settings = settings ?? [];
        RetentionPeriod = retentionPeriod;
        MaxItems = maxItems;
        EnablePersistence = enablePersistence;
        ConnectionString = connectionString;
        KeyPrefix = keyPrefix;
    }

    /// <summary>Creates an in-memory provider configuration.</summary>
    /// <param name="maxItems">Maximum number of items to store.</param>
    /// <returns>An in-memory <see cref="MemoryProviderConfig"/>.</returns>
    public static MemoryProviderConfig InMemory(int maxItems = 1000)
    {
        return new MemoryProviderConfig(MemoryDefaults.DefaultProvider, maxItems: maxItems);
    }

    /// <summary>Creates a Redis provider configuration.</summary>
    /// <param name="connectionString">The Redis connection string.</param>
    /// <param name="retention">Optional retention period.</param>
    /// <returns>A Redis <see cref="MemoryProviderConfig"/>.</returns>
    public static MemoryProviderConfig Redis(string connectionString, TimeSpan? retention = null)
    {
        return new MemoryProviderConfig(
            "Redis",
            connectionString: connectionString,
            retentionPeriod: retention,
            enablePersistence: true);
    }

    /// <summary>Creates a ChromaDB provider configuration.</summary>
    /// <param name="connectionString">The ChromaDB connection string.</param>
    /// <param name="maxItems">Maximum number of items to store.</param>
    /// <returns>A ChromaDB <see cref="MemoryProviderConfig"/>.</returns>
    public static MemoryProviderConfig ChromaDB(string connectionString, int maxItems = 10000)
    {
        return new MemoryProviderConfig(
            "ChromaDB",
            connectionString: connectionString,
            maxItems: maxItems,
            enablePersistence: true);
    }

    /// <summary>Creates a LanceDB embedded vector store provider configuration.</summary>
    /// <param name="dbPath">The file system path for the LanceDB database directory.</param>
    /// <param name="topK">Default number of results to return from queries.</param>
    /// <returns>A LanceDB <see cref="MemoryProviderConfig"/>.</returns>
    public static MemoryProviderConfig LanceDB(string dbPath = "./data/orkeon_memory", int topK = 10)
    {
        return new MemoryProviderConfig(
            "LanceDB",
            settings: new Dictionary<string, object>
            {
                ["databasePath"] = dbPath,
                ["defaultTopK"] = topK
            },
            enablePersistence: true);
    }
}
