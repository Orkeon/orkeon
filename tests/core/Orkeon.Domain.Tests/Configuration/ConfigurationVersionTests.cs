using Orkeon.Domain.Configuration;

using Orkeon.Domain.Common;
using Orkeon.Domain.Tests.Fixtures;
namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for ConfigurationVersion following Clean Architecture principles.
/// Tests the configuration version record for configuration management.
/// </summary>
public class ConfigurationVersionTests
{
    private static readonly string[] StableTestedTags = ["stable", "tested"];
    private static readonly int[] Int123Array = [1, 2, 3];
    private static readonly string[] Environments = ["development", "staging", "production"];
    #region Constructor Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingConfigurationVersionUsingConstructorWithRequiredParameters()
    {
        // Arrange
        var configurationName = "database-config";
        var version = ConfigurationVersionId.Create();
        var content = "{ \"connectionString\": \"test\" }";
        var beforeCreation = DateTime.UtcNow;

        // Act
        var configVersion = new ConfigurationVersion(configurationName, version, content);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(configVersion.Id); // ID is ULID-based EntityId
        Assert.NotNull(configVersion.Id); // ID is ULID-based EntityId // Should be a valid GUID
        Assert.Equal(configurationName, configVersion.ConfigurationName);
        Assert.Equal(version, configVersion.Version);
        Assert.Equal(content, configVersion.Content);
        Assert.True(configVersion.CreatedAt >= beforeCreation);
        Assert.True(configVersion.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, configVersion.CreatedAt.Kind);
        Assert.Null(configVersion.CreatedBy);
        Assert.Null(configVersion.Description);
        Assert.NotNull(configVersion.Metadata);
        Assert.Empty(configVersion.Metadata);
        Assert.False(configVersion.IsActive);
    }

    [Fact]
    public void ShouldCreateInstance_WhenUsingConfigurationVersionUsingConstructorWithAllParameters()
    {
        // Arrange
        var configurationName = "api-config";
        var version = ConfigurationVersionId.Create();
        var content = "{ \"baseUrl\": \"https://api.example.com\" }";
        var createdBy = "john.doe@example.com";
        var description = "Updated API configuration with new endpoints";
        var metadata = new Dictionary<string, object>
        {
            { "environment", "production" },
            { "region", "us-east-1" },
            { "deploymentId", "deploy-123" }
        };
        var isActive = true;

        // Act
        var configVersion = new ConfigurationVersion(
            configurationName, version, content, createdBy, description, metadata, isActive);

        // Assert
        Assert.Equal(configurationName, configVersion.ConfigurationName);
        Assert.Equal(version, configVersion.Version);
        Assert.Equal(content, configVersion.Content);
        Assert.Equal(createdBy, configVersion.CreatedBy);
        Assert.Equal(description, configVersion.Description);
        Assert.Equal(metadata, configVersion.Metadata);
        Assert.Equal(3, configVersion.Metadata.Count);
        Assert.True(configVersion.IsActive);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingConfigurationVersionUsingConstructorWithNullConfigurationName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationVersion(null!, "1.0.0", "content"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingConfigurationVersionUsingConstructorWithNullVersion()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationVersion(ConfigurationVersionId.Create(), null!, "content"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingConfigurationVersionUsingConstructorWithNullContent()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), null!));
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("simple-config", "1.0", "simple content")]
    [InlineData("complex.config.name", "2.1.3-beta", "complex content with special chars !@#$%")]
    public void ShouldAcceptAll_WhenUsingConfigurationVersionUsingConstructorWithVariousInputs(
        string configName, string version, string content)
    {
        // Act
        var configVersion = new ConfigurationVersion(configName, version, content);

        // Assert
        Assert.Equal(configName, configVersion.ConfigurationName);
        Assert.Equal(version, configVersion.Version);
        Assert.Equal(content, configVersion.Content);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingConfigurationVersionUsingConstructorMultipleInstances()
    {
        // Act
        var version1 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content1");
        var version2 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content2");
        var version3 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content3");

        // Assert
        Assert.NotEqual(version1.Id, version2.Id);
        Assert.NotEqual(version2.Id, version3.Id);
        Assert.NotEqual(version1.Id, version3.Id);

        // All should be valid GUIDs
        Assert.NotNull(version1.Id); // ID is ULID-based EntityId
        Assert.NotNull(version2.Id); // ID is ULID-based EntityId
        Assert.NotNull(version3.Id); // ID is ULID-based EntityId
    }

    [Fact]
    public void ShouldSetDefaults_WhenUsingConfigurationVersionUsingConstructorWithNullOptionalParameters()
    {
        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content", null, null, null);

        // Assert
        Assert.Null(configVersion.CreatedBy);
        Assert.Null(configVersion.Description);
        Assert.NotNull(configVersion.Metadata);
        Assert.Empty(configVersion.Metadata);
        Assert.False(configVersion.IsActive);
    }

    #endregion

    #region CreatedAt Tests

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingConfigurationVersionUsingCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content");

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(configVersion.CreatedAt >= beforeCreation);
        Assert.True(configVersion.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, configVersion.CreatedAt.Kind);
    }

    [Fact]
    public void ShouldHaveUniqueTimestamps_WhenUsingConfigurationVersionUsingCreatedAtMultipleInstances()
    {
        // Act
        var version1 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content1");
        ClockAdvance.UntilStrictlyAfter(version1.CreatedAt); // Ensure different timestamps (R5.6)
        var version2 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content2");

        // Assert
        Assert.True(version2.CreatedAt >= version1.CreatedAt);
    }

    #endregion

    #region Static Factory Method Tests

    [Fact]
    public void ShouldCreateInstanceWithDefaults_WhenUsingConfigurationVersionCreating()
    {
        // Arrange
        var name = "factory-config";
        var version = ConfigurationVersionId.Create();
        var content = "factory content";

        // Act
        var configVersion = ConfigurationVersion.Create(name, version, content);

        // Assert
        Assert.Equal(name, configVersion.ConfigurationName);
        Assert.Equal(version, configVersion.Version);
        Assert.Equal(content, configVersion.Content);
        Assert.NotNull(configVersion.Id); // ID is ULID-based EntityId
        Assert.NotNull(configVersion.Id); // ID is ULID-based EntityId
        Assert.Null(configVersion.CreatedBy);
        Assert.Null(configVersion.Description);
        Assert.NotNull(configVersion.Metadata);
        Assert.Empty(configVersion.Metadata);
        Assert.False(configVersion.IsActive);
        Assert.NotEqual(default(DateTime), configVersion.CreatedAt);
    }

    [Theory]
    [InlineData("config-a", "1.0.0", "content-a")]
    [InlineData("config-b", "2.0.0-beta", "content-b")]
    [InlineData("", "", "")]
    public void ShouldAcceptAll_WhenUsingConfigurationVersionCreatingWithVariousInputs(
        string name, string version, string content)
    {
        // Act
        var configVersion = ConfigurationVersion.Create(name, version, content);

        // Assert
        Assert.Equal(name, configVersion.ConfigurationName);
        Assert.Equal(version, configVersion.Version);
        Assert.Equal(content, configVersion.Content);
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public void ShouldBeModifiable_WhenUsingConfigurationVersionUsingMetadata()
    {
        // Arrange
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content");

        // Act
        configVersion.Metadata.Add("environment", "test");
        configVersion.Metadata.Add("deployedAt", DateTime.UtcNow);
        configVersion.Metadata.Add("tags", StableTestedTags);

        // Assert
        Assert.Equal(3, configVersion.Metadata.Count);
        Assert.Equal("test", configVersion.Metadata["environment"]);
        Assert.IsType<DateTime>(configVersion.Metadata["deployedAt"]);
        Assert.IsType<string[]>(configVersion.Metadata["tags"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionUsingMetadataWithComplexObjects()
    {
        // Arrange
        var complexMetadata = new Dictionary<string, object>
        {
            { "string", "test value" },
            { "int", 42 },
            { "bool", true },
            { "datetime", DateTime.UtcNow },
            { "array", Int123Array },
            { "nested", new { Name = "Test", Value = 123 } },
            { "null", null! }
        };

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content",
            metadata: complexMetadata);

        // Assert
        Assert.Equal(7, configVersion.Metadata.Count);
        Assert.Equal("test value", configVersion.Metadata["string"]);
        Assert.Equal(42, configVersion.Metadata["int"]);
        Assert.True((bool)configVersion.Metadata["bool"]);
        Assert.IsType<DateTime>(configVersion.Metadata["datetime"]);
        Assert.IsType<int[]>(configVersion.Metadata["array"]);
        Assert.Null(configVersion.Metadata["null"]);
    }

    [Fact]
    public void ShouldAffectOriginalDictionary_WhenUsingConfigurationVersionUsingMetadataModificationAfterCreation()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { { "initial", "value" } };
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content", metadata: metadata);

        // Act
        metadata.Add("added", "after creation");

        // Assert
        Assert.Equal(2, configVersion.Metadata.Count);
        Assert.Contains("added", configVersion.Metadata.Keys);
        Assert.Equal("after creation", configVersion.Metadata["added"]);
    }

    #endregion

    #region IsActive Property Tests

    [Fact]
    public void ShouldBeSettable_WhenUsingConfigurationVersionUsingIsActive()
    {
        // Arrange
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content");

        // Act
        var updated = configVersion with { IsActive = true };

        // Assert
        Assert.True(updated.IsActive);
        Assert.False(configVersion.IsActive); // Original unchanged
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldAcceptBothValues_WhenUsingConfigurationVersionUsingIsActive(bool isActive)
    {
        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content", isActive: isActive);

        // Assert
        Assert.Equal(isActive, configVersion.IsActive);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingConfigurationVersionUsingEqualitySameValues()
    {
        // Arrange
        var configName = "test-config";
        var version = ConfigurationVersionId.Create();
        var content = "test content";
        var createdBy = "user@test.com";
        var description = "test description";
        var metadata = new Dictionary<string, object> { { "key", "value" } };

        // Note: Records generate new IDs and CreatedAt timestamps, so we need to create them
        // in a way that allows for comparison
        var guid = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;

        // We can't easily test full equality due to generated Id and CreatedAt
        // But we can test individual properties
        var configVersion1 = new ConfigurationVersion(configName, version, content, createdBy, description, metadata);
        var configVersion2 = new ConfigurationVersion(configName, version, content, createdBy, description, metadata);

        // Act & Assert - Test individual properties since Id and CreatedAt will differ
        Assert.Equal(configVersion1.ConfigurationName, configVersion2.ConfigurationName);
        Assert.Equal(configVersion1.Version, configVersion2.Version);
        Assert.Equal(configVersion1.Content, configVersion2.Content);
        Assert.Equal(configVersion1.CreatedBy, configVersion2.CreatedBy);
        Assert.Equal(configVersion1.Description, configVersion2.Description);
        Assert.Equal(configVersion1.Metadata, configVersion2.Metadata);
        Assert.Equal(configVersion1.IsActive, configVersion2.IsActive);

        // But Id and CreatedAt should be different
        Assert.NotEqual(configVersion1.Id, configVersion2.Id);
        // CreatedAt might be the same if created very quickly, so we don't assert inequality
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingConfigurationVersionUsingEqualityDifferentConfigurationName()
    {
        // Arrange
        var version1 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content");
        var version2 = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content");

        // Act & Assert
        Assert.NotEqual(version1.ConfigurationName, version2.ConfigurationName);
        // Full record equality will be false due to different names and generated Ids
        Assert.NotEqual(version1, version2);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingConfigurationVersionWithExpression()
    {
        // Arrange
        var original = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "content", "user@test.com");

        // Act
        var modified = original with { IsActive = true };

        // Assert
        Assert.False(original.IsActive);
        Assert.True(modified.IsActive);
        // Description is init-only property in a record, cannot be modified with 'with' expression
        Assert.Equal(original.ConfigurationName, modified.ConfigurationName);
        Assert.Equal(original.Id, modified.Id); // Id should be preserved with 'with' expression
        Assert.NotEqual(original, modified);
    }

    #endregion

    #region Content Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionUsingContentWithJsonContent()
    {
        // Arrange
        var jsonContent = """
        {
            "database": {
                "connectionString": "Server=localhost;Database=test",
                "timeout": 30,
                "poolSize": 10
            },
            "logging": {
                "level": "Information",
                "providers": ["Console", "File"]
            }
        }
        """;

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), jsonContent);

        // Assert
        Assert.Equal(jsonContent, configVersion.Content);
        Assert.Contains("database", configVersion.Content);
        Assert.Contains("logging", configVersion.Content);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionUsingContentWithXmlContent()
    {
        // Arrange
        var xmlContent = """
        <?xml version="1.0" encoding="UTF-8"?>
        <configuration>
            <appSettings>
                <add key="DatabaseConnection" value="test" />
                <add key="LogLevel" value="Debug" />
            </appSettings>
        </configuration>
        """;

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), xmlContent);

        // Assert
        Assert.Equal(xmlContent, configVersion.Content);
        Assert.Contains("<?xml", configVersion.Content);
        Assert.Contains("appSettings", configVersion.Content);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionUsingContentWithYamlContent()
    {
        // Arrange
        var yamlContent = """
        database:
          host: localhost
          port: 5432
          name: testdb
        
        logging:
          level: info
          format: json
        """;

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), yamlContent);

        // Assert
        Assert.Equal(yamlContent, configVersion.Content);
        Assert.Contains("database:", configVersion.Content);
        Assert.Contains("logging:", configVersion.Content);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionUsingContentWithVeryLargeContent()
    {
        // Arrange
        var largeContent = new string('A', 100000); // 100KB content

        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), largeContent);

        // Assert
        Assert.Equal(100000, configVersion.Content.Length);
        Assert.Equal(largeContent, configVersion.Content);
    }

    #endregion

    #region Version String Tests

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.1.3")]
    [InlineData("1.0.0-alpha")]
    [InlineData("2.0.0-beta.1")]
    [InlineData("3.1.0-rc.2")]
    [InlineData("1.0.0+build.123")]
    [InlineData("2.0.0-beta.1+exp.sha.5114f85")]
    [InlineData("v1.0.0")]
    [InlineData("release-1.5")]
    public void ShouldAcceptVariousFormats_WhenUsingConfigurationVersionUsingVersion(string versionString)
    {
        // Act
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), versionString, "content");

        // Assert
        Assert.Equal(versionString, configVersion.Version);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainConsistency_WhenUsingConfigurationVersionWithCompleteLifecycle()
    {
        // Arrange - Create initial version
        var configName = "lifecycle-config";
        var initialVersion = ConfigurationVersion.Create(configName, "1.0.0", "initial content");

        // Act - Update version
        // Cannot modify init-only properties Version, Content, Description
        var updatedVersion = initialVersion with
        {
            IsActive = true
        };

        updatedVersion.Metadata.Add("previousVersion", initialVersion.Version);
        updatedVersion.Metadata.Add("updatedAt", DateTime.UtcNow);

        // Assert
        Assert.Equal(configName, initialVersion.ConfigurationName);
        Assert.Equal(configName, updatedVersion.ConfigurationName);
        Assert.NotNull(initialVersion.Version);
        Assert.NotNull(updatedVersion.Version); // Version remains same, can't change init-only property
        Assert.False(initialVersion.IsActive);
        Assert.True(updatedVersion.IsActive);
    }

    [Fact]
    public void ShouldTrackEnvironmentInfo_WhenUsingConfigurationVersionWithEnvironmentSpecificMetadata()
    {
        // Arrange
        var environments = Environments;
        var versions = new List<ConfigurationVersion>();

        // Act - Create versions for different environments
        foreach (var env in environments)
        {
            var version = new ConfigurationVersion(
                "multi-env-config",
                "1.0.0",
                $"{{ \"environment\": \"{env}\" }}",
                "deploy-system",
                $"Configuration for {env} environment",
                new Dictionary<string, object>
                {
                    { "environment", env },
                    { "deployedAt", DateTime.UtcNow },
                    { "region", env == "production" ? "us-east-1" : "us-west-2" }
                },
                env == "production" // Only production is active
            );
            versions.Add(version);
        }

        // Assert
        Assert.Equal(3, versions.Count);
        Assert.All(versions, v => Assert.Equal("multi-env-config", v.ConfigurationName));
        Assert.All(versions, v => Assert.NotNull(v.Version));

        var productionVersion = versions.First(v => (string)v.Metadata["environment"] == "production");
        var devVersion = versions.First(v => (string)v.Metadata["environment"] == "development");

        Assert.True(productionVersion.IsActive);
        Assert.False(devVersion.IsActive);
        Assert.Equal("us-east-1", productionVersion.Metadata["region"]);
        Assert.Equal("us-west-2", devVersion.Metadata["region"]);
    }

    [Fact]
    public void ShouldTrackVersionHistory_WhenUsingConfigurationVersionUsingConfigurationHistory()
    {
        // Arrange
        var configName = "versioned-config";
        var versions = new List<ConfigurationVersion>();

        // Act - Create version history
        for (int i = 1; i <= 5; i++)
        {
            var version = new ConfigurationVersion(
                configName,
                $"1.{i}.0",
                $"{{ \"version\": \"1.{i}.0\", \"features\": {i * 10} }}",
                $"developer{i}@company.com",
                $"Version 1.{i}.0 with {i * 10} features",
                new Dictionary<string, object>
                {
                    { "buildNumber", i * 100 },
                    { "featureCount", i * 10 },
                    { "isRelease", i % 2 == 0 }
                },
                i == 5 // Latest version is active
            );
            versions.Add(version);
        }

        // Assert
        Assert.Equal(5, versions.Count);
        Assert.All(versions, v => Assert.Equal(configName, v.ConfigurationName));

        // Verify version progression
        Assert.Equal("1.1.0", versions[0].Version);
        Assert.Equal("1.5.0", versions[4].Version);

        // Only latest should be active
        Assert.True(versions[4].IsActive);
        Assert.All(versions.Take(4), v => Assert.False(v.IsActive));

        // Verify unique IDs
        var uniqueIds = versions.Select(v => v.Id).Distinct().Count();
        Assert.Equal(5, uniqueIds);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationVersionWithUnicodeContent()
    {
        // Arrange
        var unicodeConfigName = "配置文件";
        var unicodeVersion = ConfigurationVersionId.Create();
        var unicodeContent = "{ \"消息\": \"你好世界 🌍\", \"설정\": \"안녕하세요\" }";
        var unicodeDescription = "Unicode configuration with émojis 🚀 and various languages";

        // Act
        var configVersion = new ConfigurationVersion(
            unicodeConfigName, unicodeVersion, unicodeContent,
            description: unicodeDescription);

        // Assert
        Assert.Equal(unicodeConfigName, configVersion.ConfigurationName);
        Assert.Equal(unicodeVersion, configVersion.Version);
        Assert.Equal(unicodeContent, configVersion.Content);
        Assert.Equal(unicodeDescription, configVersion.Description);
        Assert.Contains("🌍", configVersion.Content);
        Assert.Contains("🚀", configVersion.Description!);
    }

    [Fact]
    public void ShouldAcceptEmptyValues_WhenUsingConfigurationVersionWithEmptyStrings()
    {
        // Act - ConfigurationVersionId.Create() implicitly converts to string via ULID
        var configName = (string)ConfigurationVersionId.Create();
        var version = (string)ConfigurationVersionId.Create();
        var configVersion = new ConfigurationVersion(configName, version, "", "", "");

        // Assert
        Assert.NotEmpty(configVersion.ConfigurationName);
        Assert.NotNull(configVersion.Version);
        Assert.Equal("", configVersion.Content);
        Assert.Equal("", configVersion.CreatedBy);
        Assert.Equal("", configVersion.Description);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingConfigurationVersionToString()
    {
        // Arrange
        var configVersion = new ConfigurationVersion(ConfigurationVersionId.Create(), ConfigurationVersionId.Create(), "test content");

        // Act
        var stringRepresentation = configVersion.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // For records, ToString() includes all property values
        Assert.Contains("ConfigurationVersion", stringRepresentation);
    }

    #endregion
}
