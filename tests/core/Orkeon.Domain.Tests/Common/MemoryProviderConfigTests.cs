using Orkeon.Domain.Memory;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for MemoryProviderConfig following Clean Architecture principles.
/// Tests the business rules and validation logic of the MemoryProviderConfig record.
/// </summary>
public class MemoryProviderConfigTests
{
    [Fact]
    public void ShouldCreateConfig_WhenConstructingWithValidParameters()
    {
        // Arrange
        var providerType = "CustomProvider";
        var settings = new Dictionary<string, object>
        {
            { "host", "localhost" },
            { "port", 6379 }
        };
        var retentionPeriod = TimeSpan.FromDays(30);
        var maxItems = 5000;
        var enablePersistence = true;
        var connectionString = "server=localhost;port=6379";

        // Act
        var config = new MemoryProviderConfig(
            providerType,
            settings,
            retentionPeriod,
            maxItems,
            enablePersistence,
            connectionString);

        // Assert
        Assert.Equal(providerType, config.ProviderType);
        Assert.Equal(settings, config.Settings);
        Assert.Equal(retentionPeriod, config.RetentionPeriod);
        Assert.Equal(maxItems, config.MaxItems);
        Assert.Equal(enablePersistence, config.EnablePersistence);
        Assert.Equal(connectionString, config.ConnectionString);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullProviderType()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MemoryProviderConfig(null!));

        Assert.Equal("providerType", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyDictionary_WhenConstructingWithNullSettings()
    {
        // Act
        var config = new MemoryProviderConfig("Provider", settings: null);

        // Assert
        Assert.NotNull(config.Settings);
        Assert.Empty(config.Settings);
    }

    [Fact]
    public void ShouldUseDefaults_WhenConstructingWithOptionalParameters()
    {
        // Act
        var config = new MemoryProviderConfig("BasicProvider");

        // Assert
        Assert.Equal("BasicProvider", config.ProviderType);
        Assert.Empty(config.Settings);
        Assert.Null(config.RetentionPeriod);
        Assert.Null(config.MaxItems);
        Assert.False(config.EnablePersistence);
        Assert.Null(config.ConnectionString);
    }

    [Fact]
    public void ShouldCreateInMemoryConfig_WhenUsingInMemory()
    {
        // Act
        var config = MemoryProviderConfig.InMemory();

        // Assert
        Assert.Equal("InMemory", config.ProviderType);
        Assert.Equal(1000, config.MaxItems);
        Assert.False(config.EnablePersistence);
        Assert.Null(config.ConnectionString);
        Assert.Null(config.RetentionPeriod);
    }

    [Fact]
    public void ShouldUseProvidedValue_WhenUsingInMemoryWithCustomMaxItems()
    {
        // Arrange
        var maxItems = 5000;

        // Act
        var config = MemoryProviderConfig.InMemory(maxItems);

        // Assert
        Assert.Equal("InMemory", config.ProviderType);
        Assert.Equal(maxItems, config.MaxItems);
    }

    [Fact]
    public void ShouldCreateRedisConfig_WhenUsingRedis()
    {
        // Arrange
        var connectionString = "localhost:6379,password=test123";

        // Act
        var config = MemoryProviderConfig.Redis(connectionString);

        // Assert
        Assert.Equal("Redis", config.ProviderType);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.True(config.EnablePersistence);
        Assert.Null(config.RetentionPeriod);
    }

    [Fact]
    public void ShouldIncludeRetentionPeriod_WhenUsingRedisWithRetention()
    {
        // Arrange
        var connectionString = "localhost:6379";
        var retention = TimeSpan.FromDays(7);

        // Act
        var config = MemoryProviderConfig.Redis(connectionString, retention);

        // Assert
        Assert.Equal("Redis", config.ProviderType);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Equal(retention, config.RetentionPeriod);
        Assert.True(config.EnablePersistence);
    }

    [Fact]
    public void ShouldCreateChromaDBConfig_WhenUsingChromaDB()
    {
        // Arrange
        var connectionString = "http://localhost:8000";

        // Act
        var config = MemoryProviderConfig.ChromaDB(connectionString);

        // Assert
        Assert.Equal("ChromaDB", config.ProviderType);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Equal(10000, config.MaxItems);
        Assert.True(config.EnablePersistence);
    }

    [Fact]
    public void ShouldUseProvidedValue_WhenUsingChromaDBWithCustomMaxItems()
    {
        // Arrange
        var connectionString = "http://localhost:8000";
        var maxItems = 50000;

        // Act
        var config = MemoryProviderConfig.ChromaDB(connectionString, maxItems);

        // Assert
        Assert.Equal("ChromaDB", config.ProviderType);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Equal(maxItems, config.MaxItems);
        Assert.True(config.EnablePersistence);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var settings = new Dictionary<string, object> { { "key", "value" } };
        var config1 = new MemoryProviderConfig(
            "Provider",
            settings,
            TimeSpan.FromHours(1),
            100,
            true,
            "connection");

        var config2 = new MemoryProviderConfig(
            "Provider",
            settings,
            TimeSpan.FromHours(1),
            100,
            true,
            "connection");

        // Act & Assert
        Assert.Equal(config1, config2);
        Assert.True(config1.Equals(config2));
        Assert.Equal(config1.GetHashCode(), config2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var config1 = MemoryProviderConfig.InMemory(1000);
        var config2 = MemoryProviderConfig.InMemory(2000);

        // Act & Assert
        Assert.NotEqual(config1, config2);
        Assert.False(config1.Equals(config2));
    }

    [Theory]
    [InlineData("InMemory", false)]
    [InlineData("Redis", true)]
    [InlineData("ChromaDB", true)]
    public void ShouldSetCorrectPersistence_WhenUsingFactoryMethods(string expectedType, bool expectedPersistence)
    {
        // Act
        var config = expectedType switch
        {
            "InMemory" => MemoryProviderConfig.InMemory(),
            "Redis" => MemoryProviderConfig.Redis("connection"),
            "ChromaDB" => MemoryProviderConfig.ChromaDB("connection"),
            _ => throw new ArgumentException($"Unknown provider type: {expectedType}", nameof(expectedType))
        };

        // Assert
        Assert.Equal(expectedType, config.ProviderType);
        Assert.Equal(expectedPersistence, config.EnablePersistence);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingSettings()
    {
        // Arrange
        var config = new MemoryProviderConfig("Provider");

        // Act
        config.Settings.Add("newKey", "newValue");
        config.Settings["anotherKey"] = 123;

        // Assert
        Assert.Equal(2, config.Settings.Count);
        Assert.Equal("newValue", config.Settings["newKey"]);
        Assert.Equal(123, config.Settings["anotherKey"]);
    }
}
