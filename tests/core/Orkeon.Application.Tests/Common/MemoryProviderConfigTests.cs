using Orkeon.Application.Memory;

namespace Orkeon.Application.Tests.Common;

public class MemoryProviderConfigDtoTests
{
    private static readonly int[] SampleIntArray = [1, 2, 3];

    [Fact]
    public void ShouldCreateConfig_WhenConstructingWithRequiredParameters()
    {
        // Arrange
        var type = "CustomProvider";
        var connectionString = "server=localhost;port=5432";

        // Act
        var config = new MemoryProviderConfigDto(type, connectionString);

        // Assert
        Assert.Equal(type, config.Type);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Null(config.Options);
    }

    [Fact]
    public void ShouldCreateConfig_WhenConstructingWithAllParameters()
    {
        // Arrange
        var type = "CustomProvider";
        var connectionString = "server=localhost;port=5432";
        var options = new Dictionary<string, object>
        {
            { "poolSize", 10 },
            { "timeout", 30 },
            { "enableSSL", true }
        };

        // Act
        var config = new MemoryProviderConfigDto(type, connectionString, options);

        // Assert
        Assert.Equal(type, config.Type);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Equal(options, config.Options);
        Assert.Equal(3, config.Options!.Count);
    }

    [Fact]
    public void ShouldAcceptEmptyDictionary_WhenConstructingWithEmptyOptions()
    {
        // Arrange
        var options = new Dictionary<string, object>();

        // Act
        var config = new MemoryProviderConfigDto("Provider", "connection", options);

        // Assert
        Assert.NotNull(config.Options);
        Assert.Empty(config.Options);
    }

    [Fact]
    public void ShouldReturnCorrectConfig_WhenUsingDefaultsInMemory()
    {
        // Act
        var config = MemoryProviderConfigDefaults.InMemory;

        // Assert
        Assert.Equal("InMemory", config.Type);
        Assert.Equal(string.Empty, config.ConnectionString);
        Assert.Null(config.Options);
    }

    [Fact]
    public void ShouldReturnCorrectConfig_WhenUsingDefaultsRedis()
    {
        // Arrange
        var connectionString = "localhost:6379,password=test123";

        // Act
        var config = MemoryProviderConfigDefaults.Redis(connectionString);

        // Assert
        Assert.Equal("Redis", config.Type);
        Assert.Equal(connectionString, config.ConnectionString);
        Assert.Null(config.Options);
    }

    [Fact]
    public void ShouldReturnCorrectConfig_WhenUsingDefaultsChromaDB()
    {
        // Arrange
        var endpoint = "http://localhost:8000";
        var apiKey = "test-api-key-123";

        // Act
        var config = MemoryProviderConfigDefaults.ChromaDB(endpoint, apiKey);

        // Assert
        Assert.Equal("ChromaDB", config.Type);
        Assert.Equal(endpoint, config.ConnectionString);
        Assert.NotNull(config.Options);
        Assert.Single(config.Options);
        Assert.True(config.Options.ContainsKey("ApiKey"));
        Assert.Equal(apiKey, config.Options["ApiKey"]);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var options = new Dictionary<string, object> { { "key", "value" } };
        var config1 = new MemoryProviderConfigDto("Provider", "connection", options);
        var config2 = new MemoryProviderConfigDto("Provider", "connection", options);

        // Act & Assert
        Assert.Equal(config1, config2);
        Assert.True(config1 == config2);
        Assert.False(config1 != config2);
        Assert.Equal(config1.GetHashCode(), config2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var config1 = new MemoryProviderConfigDto("Provider1", "connection1");
        var config2 = new MemoryProviderConfigDto("Provider2", "connection2");

        // Act & Assert
        Assert.NotEqual(config1, config2);
        Assert.False(config1 == config2);
        Assert.True(config1 != config2);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenRecordingEqualityWithNullOptions()
    {
        // Arrange
        var config1 = new MemoryProviderConfigDto("Provider", "connection", null);
        var config2 = new MemoryProviderConfigDto("Provider", "connection", null);
        var config3 = new MemoryProviderConfigDto("Provider", "connection", []);

        // Act & Assert
        Assert.Equal(config1, config2);
        Assert.NotEqual(config1, config3);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = new MemoryProviderConfigDto("Original", "original-connection");
        var newOptions = new Dictionary<string, object> { { "modified", true } };

        // Act
        var modified = original with
        {
            Type = "Modified",
            Options = newOptions
        };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.Equal("Original", original.Type);
        Assert.Equal("Modified", modified.Type);
        Assert.Equal(original.ConnectionString, modified.ConnectionString);
        Assert.Null(original.Options);
        Assert.Equal(newOptions, modified.Options);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var options = new Dictionary<string, object> { { "test", "value" } };
        var config = new MemoryProviderConfigDto("TestProvider", "test-connection", options);

        // Act
        var (type, connectionString, opts) = config;

        // Assert
        Assert.Equal("TestProvider", type);
        Assert.Equal("test-connection", connectionString);
        Assert.Equal(options, opts);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var config = new MemoryProviderConfigDto(
            "TestProvider",
            "server=localhost",
            new Dictionary<string, object> { { "option1", "value1" } });

        // Act
        var result = config.ToString();

        // Assert
        Assert.Contains("TestProvider", result);
        Assert.Contains("server=localhost", result);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingOptionsWithComplexTypes()
    {
        // Arrange
        var options = new Dictionary<string, object>
        {
            { "string", "value" },
            { "number", 42 },
            { "decimal", 3.14 },
            { "bool", true },
            { "array", SampleIntArray },
            { "nested", new Dictionary<string, string> { { "inner", "value" } } }
        };

        // Act
        var config = new MemoryProviderConfigDto("Provider", "connection", options);

        // Assert
        Assert.Equal(6, config.Options?.Count);
        Assert.Equal("value", config.Options?["string"]);
        Assert.Equal(42, config.Options?["number"]);
        Assert.Equal(3.14, config.Options?["decimal"]);
        Assert.True((bool)config.Options?["bool"]!);
        Assert.IsType<int[]>(config.Options?["array"]);
        Assert.IsType<Dictionary<string, string>>(config.Options?["nested"]);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingDefaultsInMemoryMultipleAccesses()
    {
        // Act
        var config1 = MemoryProviderConfigDefaults.InMemory;
        var config2 = MemoryProviderConfigDefaults.InMemory;

        // Assert
        Assert.Same(config1, config2);
    }

    [Fact]
    public void ShouldConfigurationEvolution_WhenUsingComplexScenario()
    {
        // Start with basic Redis config
        var basicRedis = MemoryProviderConfigDefaults.Redis("localhost:6379");

        // Enhance with options
        var enhancedRedis = basicRedis with
        {
            Options = new Dictionary<string, object>
            {
                { "database", 0 },
                { "connectTimeout", 5000 },
                { "syncTimeout", 1000 },
                { "allowAdmin", false }
            }
        };

        // Create a ChromaDB config with custom options
        var chromaConfig = new MemoryProviderConfigDto(
            "ChromaDB",
            "http://vectordb.internal:8000",
            new Dictionary<string, object>
            {
                { "ApiKey", "secure-key" },
                { "Collection", "knowledge_base" },
                { "EmbeddingDimension", 768 }
            });

        // Verify configurations
        Assert.Equal("Redis", basicRedis.Type);
        Assert.Null(basicRedis.Options);

        Assert.Equal("Redis", enhancedRedis.Type);
        Assert.Equal(4, enhancedRedis.Options?.Count);
        Assert.Equal(0, enhancedRedis.Options?["database"]);

        Assert.Equal("ChromaDB", chromaConfig.Type);
        Assert.Equal(3, chromaConfig.Options?.Count);
        Assert.Equal("knowledge_base", chromaConfig.Options?["Collection"]);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingEmptyConnectionString()
    {
        // Act
        var config = new MemoryProviderConfigDto("Provider", "");

        // Assert
        Assert.Equal("", config.ConnectionString);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingNullConnectionString()
    {
        // Act
        var config = new MemoryProviderConfigDto("Provider", null!);

        // Assert
        Assert.Null(config.ConnectionString);
    }
}
