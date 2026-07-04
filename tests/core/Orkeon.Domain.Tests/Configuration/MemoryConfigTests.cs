using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for Memory Configuration following Clean Architecture principles.
/// Tests the memory configuration record and its behavior.
/// </summary>
public class MemoryConfigTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingMemoryConfigWithDefaultConstructor()
    {
        var config = new MemoryConfig();
        Assert.True(config.Enabled);
        Assert.Equal(MemoryStorageType.InMemory, config.Type);
        Assert.Equal("InMemory", config.Provider);
        Assert.NotNull(config.Settings);
        Assert.Empty(config.Settings);
        Assert.Null(config.RetentionPeriod);
        Assert.Equal(1000, config.MaxItems);
        Assert.False(config.PersistToDisk);
        Assert.Null(config.StoragePath);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingMemoryConfigUsingProperties()
    {
        var settings = new Dictionary<string, object>
        {
            { "connectionString", "localhost:6379" },
            { "database", 0 },
            { "password", "secret" }
        };
        var retentionPeriod = TimeSpan.FromDays(30);

        var config = new MemoryConfig
        {
            Enabled = false,
            Type = MemoryStorageType.Redis,
            Provider = "Redis",
            Settings = settings,
            RetentionPeriod = retentionPeriod,
            MaxItems = 5000,
            PersistToDisk = true,
            StoragePath = "/var/lib/orkeon/memory"
        };

        Assert.False(config.Enabled);
        Assert.Equal(MemoryStorageType.Redis, config.Type);
        Assert.Equal("Redis", config.Provider);
        Assert.Equal(settings, config.Settings);
        Assert.Equal(3, config.Settings.Count);
        Assert.Equal(retentionPeriod, config.RetentionPeriod);
        Assert.Equal(5000, config.MaxItems);
        Assert.True(config.PersistToDisk);
        Assert.Equal("/var/lib/orkeon/memory", config.StoragePath);
    }

    #endregion

    #region MemoryStorageType Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingMemoryType()
    {
        var expectedValues = new[] { MemoryStorageType.InMemory, MemoryStorageType.Redis, MemoryStorageType.SQLite, MemoryStorageType.ChromaDB, MemoryStorageType.Pinecone, MemoryStorageType.Custom };
        foreach (var v in expectedValues) Assert.True(Enum.IsDefined(v));
        Assert.Equal(expectedValues.Length, Enum.GetValues<MemoryStorageType>().Length);
    }

    [Theory]
    [InlineData(MemoryStorageType.InMemory)]
    [InlineData(MemoryStorageType.Redis)]
    [InlineData(MemoryStorageType.SQLite)]
    [InlineData(MemoryStorageType.ChromaDB)]
    [InlineData(MemoryStorageType.Pinecone)]
    [InlineData(MemoryStorageType.Custom)]
    public void ShouldAcceptAllMemoryTypes_WhenUsingMemoryConfigUsingType(MemoryStorageType memoryType)
    {
        var config = new MemoryConfig { Type = memoryType };
        Assert.Equal(memoryType, config.Type);
    }

    [Fact]
    public void ShouldBeInMemory_WhenUsingMemoryTypeWithDefaultValue()
    {
        Assert.Equal(MemoryStorageType.InMemory, default(MemoryStorageType));
    }

    #endregion

    #region Settings Dictionary Tests

    [Fact]
    public void ShouldSupportInitialization_WhenUsingMemoryConfigUsingSettings()
    {
        var config = new MemoryConfig
        {
            Settings = new Dictionary<string, object>
            {
                { "host", "localhost" },
                { "port", 6379 },
                { "ssl", true },
                { "timeout", TimeoutQuick }
            }
        };
        Assert.Equal(4, config.Settings.Count);
        Assert.Equal("localhost", config.Settings["host"]);
        Assert.Equal(6379, config.Settings["port"]);
        Assert.True((bool)config.Settings["ssl"]);
        Assert.IsType<TimeSpan>(config.Settings["timeout"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryConfigUsingSettingsWithComplexObjects()
    {
        var connectionSettings = new { Primary = "server1:6379", Replicas = new[] { "server2:6379", "server3:6379" }, ReadPreference = "nearest" };
        var config = new MemoryConfig
        {
            Settings = new Dictionary<string, object>
            {
                { "cluster", connectionSettings },
                { "authToken", "bearer-token-123" },
                { "compression", new { enabled = true, level = 5 } }
            }
        };
        Assert.Equal(3, config.Settings.Count);
        Assert.Equal(connectionSettings, config.Settings["cluster"]);
    }

    #endregion

    #region Provider Configuration Tests

    [Theory]
    [InlineData(MemoryStorageType.InMemory, "InMemory")]
    [InlineData(MemoryStorageType.Redis, "Redis")]
    [InlineData(MemoryStorageType.SQLite, "SQLite")]
    [InlineData(MemoryStorageType.ChromaDB, "ChromaDB")]
    [InlineData(MemoryStorageType.Pinecone, "Pinecone")]
    [InlineData(MemoryStorageType.Custom, "CustomProvider")]
    public void ShouldBeConsistent_WhenUsingMemoryConfigUsingProviderAndType(MemoryStorageType type, string provider)
    {
        var config = new MemoryConfig { Type = type, Provider = provider };
        Assert.Equal(type, config.Type);
        Assert.Equal(provider, config.Provider);
    }

    [Fact]
    public void ShouldCanBeDifferentFromType_WhenUsingMemoryConfigUsingProvider()
    {
        var config = new MemoryConfig { Type = MemoryStorageType.Redis, Provider = "StackExchange.Redis" };
        Assert.Equal(MemoryStorageType.Redis, config.Type);
        Assert.Equal("StackExchange.Redis", config.Provider);
    }

    #endregion

    #region RetentionPeriod Tests

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    [InlineData(168)]
    [InlineData(720)]
    [InlineData(8760)]
    public void ShouldAcceptVariousDurations_WhenUsingMemoryConfigUsingRetentionPeriod(int hours)
    {
        var retention = TimeSpan.FromHours(hours);
        var config = new MemoryConfig { RetentionPeriod = retention };
        Assert.Equal(retention, config.RetentionPeriod);
    }

    [Fact]
    public void ShouldCanBeNull_WhenUsingMemoryConfigUsingRetentionPeriod()
    {
        var config = new MemoryConfig { RetentionPeriod = null };
        Assert.Null(config.RetentionPeriod);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMemoryConfigUsingRetentionPeriodWithZeroTimeSpan()
    {
        var config = new MemoryConfig { RetentionPeriod = TimeSpan.Zero };
        Assert.Equal(TimeSpan.Zero, config.RetentionPeriod);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMemoryConfigUsingRetentionPeriodWithNegativeTimeSpan()
    {
        var negativeRetention = TimeSpan.FromHours(-24);
        var config = new MemoryConfig { RetentionPeriod = negativeRetention };
        Assert.Equal(negativeRetention, config.RetentionPeriod);
    }

    #endregion

    #region MaxItems Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void ShouldAcceptVariousValues_WhenUsingMemoryConfigWithMaxItems(int maxItems)
    {
        var config = new MemoryConfig { MaxItems = maxItems };
        Assert.Equal(maxItems, config.MaxItems);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMemoryConfigWithMaxItemsWithNegativeValue()
    {
        var config = new MemoryConfig { MaxItems = -100 };
        Assert.Equal(-100, config.MaxItems);
    }

    #endregion

    #region PersistToDisk and StoragePath Tests

    [Fact]
    public void ShouldWorkWithStoragePath_WhenUsingMemoryConfigPersistingToDiskWhenEnabled()
    {
        var config = new MemoryConfig { PersistToDisk = true, StoragePath = "/data/orkeon/memory" };
        Assert.True(config.PersistToDisk);
        Assert.Equal("/data/orkeon/memory", config.StoragePath);
    }

    [Theory]
    [InlineData("/tmp/memory")]
    [InlineData("C:\\Orkeon\\Memory")]
    [InlineData("./relative/path")]
    [InlineData("~/home/user/memory")]
    [InlineData("")]
    public void ShouldAcceptVariousPaths_WhenUsingMemoryConfigUsingStoragePath(string path)
    {
        var config = new MemoryConfig { StoragePath = path };
        Assert.Equal(path, config.StoragePath);
    }

    [Fact]
    public void ShouldCanBeNullWhenNotPersisting_WhenUsingMemoryConfigUsingStoragePath()
    {
        var config = new MemoryConfig { PersistToDisk = false, StoragePath = null };
        Assert.False(config.PersistToDisk);
        Assert.Null(config.StoragePath);
    }

    #endregion

    #region Configuration Scenarios Tests

    [Fact]
    public void ShouldHaveTypicalSettings_WhenUsingMemoryConfigInMemoryConfiguration()
    {
        var config = new MemoryConfig { Enabled = true, Type = MemoryStorageType.InMemory, Provider = "InMemory", MaxItems = 1000, PersistToDisk = false };
        Assert.True(config.Enabled);
        Assert.Equal(MemoryStorageType.InMemory, config.Type);
        Assert.False(config.PersistToDisk);
        Assert.Null(config.StoragePath);
    }

    [Fact]
    public void ShouldIncludeConnectionSettings_WhenUsingMemoryConfigUsingRedisConfiguration()
    {
        var config = new MemoryConfig
        {
            Enabled = true,
            Type = MemoryStorageType.Redis,
            Provider = "Redis",
            Settings = new Dictionary<string, object> { { "connectionString", "localhost:6379,password=secret" }, { "database", 0 }, { "connectTimeout", 5000 }, { "syncTimeout", 5000 } },
            RetentionPeriod = TimeSpan.FromDays(30),
            MaxItems = 10000
        };
        Assert.Equal(MemoryStorageType.Redis, config.Type);
        Assert.Equal(4, config.Settings.Count);
        Assert.Contains("connectionString", config.Settings.Keys);
        Assert.Equal(TimeSpan.FromDays(30), config.RetentionPeriod);
    }

    [Fact]
    public void ShouldIncludeDatabasePath_WhenUsingMemoryConfigUsingSQLiteConfiguration()
    {
        var config = new MemoryConfig
        {
            Enabled = true,
            Type = MemoryStorageType.SQLite,
            Provider = "SQLite",
            Settings = new Dictionary<string, object> { { "databasePath", "/var/lib/orkeon/memory.db" }, { "journalMode", "WAL" }, { "cacheSize", 10000 } },
            PersistToDisk = true,
            StoragePath = "/var/lib/orkeon"
        };
        Assert.Equal(MemoryStorageType.SQLite, config.Type);
        Assert.True(config.PersistToDisk);
        Assert.Equal("/var/lib/orkeon", config.StoragePath);
        Assert.Equal("/var/lib/orkeon/memory.db", config.Settings["databasePath"]);
    }

    [Fact]
    public void ShouldIncludeEmbeddingSettings_WhenUsingMemoryConfigUsingVectorDatabaseConfiguration()
    {
        var config = new MemoryConfig
        {
            Enabled = true,
            Type = MemoryStorageType.Pinecone,
            Provider = "Pinecone",
            Settings = new Dictionary<string, object> { { "apiKey", "pinecone-api-key" }, { "environment", "us-west1-gcp" }, { "indexName", "orkeon-memory" }, { "dimension", 1536 }, { "metric", "cosine" }, { "podType", "p1" } },
            MaxItems = 100000
        };
        Assert.Equal(MemoryStorageType.Pinecone, config.Type);
        Assert.Equal(6, config.Settings.Count);
        Assert.Equal(1536, config.Settings["dimension"]);
        Assert.Equal("cosine", config.Settings["metric"]);
    }

    [Fact]
    public void ShouldAllowFlexibleConfiguration_WhenUsingMemoryConfigWithCustomProvider()
    {
        var config = new MemoryConfig
        {
            Enabled = true,
            Type = MemoryStorageType.Custom,
            Provider = "MyCustomMemoryProvider",
            Settings = new Dictionary<string, object> { { "customSetting1", "value1" }, { "customSetting2", 42 }, { "customSetting3", true }, { "nestedSettings", new { option1 = "a", option2 = "b" } } }
        };
        Assert.Equal(MemoryStorageType.Custom, config.Type);
        Assert.Equal("MyCustomMemoryProvider", config.Provider);
        Assert.Equal(4, config.Settings.Count);
    }

    [Fact]
    public void ShouldStillHaveValidSettings_WhenUsingMemoryConfigUsingDisabledConfiguration()
    {
        var config = new MemoryConfig
        {
            Enabled = false,
            Type = MemoryStorageType.Redis,
            Provider = "Redis",
            Settings = new Dictionary<string, object> { { "host", "localhost" } }
        };
        Assert.False(config.Enabled);
        Assert.Equal(MemoryStorageType.Redis, config.Type);
        Assert.NotEmpty(config.Settings);
    }

    #endregion

    #region TelemetryLogLevel Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingLogLevel()
    {
        var expectedValues = new[] { TelemetryLogLevel.Trace, TelemetryLogLevel.Debug, TelemetryLogLevel.Information, TelemetryLogLevel.Warning, TelemetryLogLevel.Error, TelemetryLogLevel.Critical };
        foreach (var v in expectedValues) Assert.True(Enum.IsDefined(v));
        Assert.Equal(expectedValues.Length, Enum.GetValues<TelemetryLogLevel>().Length);
    }

    [Fact]
    public void ShouldBeInOrder_WhenUsingLogLevelUsingNumericValues()
    {
        Assert.True((int)TelemetryLogLevel.Trace < (int)TelemetryLogLevel.Debug);
        Assert.True((int)TelemetryLogLevel.Debug < (int)TelemetryLogLevel.Information);
        Assert.True((int)TelemetryLogLevel.Information < (int)TelemetryLogLevel.Warning);
        Assert.True((int)TelemetryLogLevel.Warning < (int)TelemetryLogLevel.Error);
        Assert.True((int)TelemetryLogLevel.Error < (int)TelemetryLogLevel.Critical);
    }

    [Fact]
    public void ShouldBeTrace_WhenUsingLogLevelWithDefaultValue()
    {
        Assert.Equal(TelemetryLogLevel.Trace, default(TelemetryLogLevel));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryConfigWithNullSettings()
    {
        var config = new MemoryConfig { Settings = null! };
        Assert.Null(config.Settings);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMemoryConfigWithEmptyProvider()
    {
        var config = new MemoryConfig { Provider = "" };
        Assert.Equal("", config.Provider);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMemoryConfigWithNullProvider()
    {
        var config = new MemoryConfig { Provider = null! };
        Assert.Null(config.Provider);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryConfigWithUnicodeStoragePath()
    {
        var config = new MemoryConfig { StoragePath = "/数据/crew记忆" };
        Assert.Contains("数据", config.StoragePath);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingMemoryConfigToString()
    {
        var config = new MemoryConfig { Type = MemoryStorageType.Redis };
        var stringRepresentation = config.ToString();
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("MemoryConfig", stringRepresentation);
    }

    #endregion
}
