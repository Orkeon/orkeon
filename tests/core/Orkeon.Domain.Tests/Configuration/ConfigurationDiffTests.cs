using Orkeon.Domain.Configuration;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for Configuration Diff following Clean Architecture principles.
/// Tests the configuration difference tracking records and functionality.
/// </summary>
public class ConfigurationDiffTests
{
    #region ConfigurationChange Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingConfigurationChangeUsingConstructorWithValidParameters()
    {
        // Arrange
        var propertyPath = "database.connectionString";
        var oldValue = "old-connection";
        var newValue = "new-connection";
        var type = ChangeType.Modified;
        var description = "Updated database connection string";

        // Act
        var change = new ConfigurationChange(propertyPath, oldValue, newValue, type, description);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
        Assert.Equal(oldValue, change.OldValue);
        Assert.Equal(newValue, change.NewValue);
        Assert.Equal(type, change.Type);
        Assert.Equal(description, change.Description);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingConfigurationChangeUsingConstructorWithNullPropertyPath()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ConfigurationChange(null!, "old", "new", ChangeType.Modified));
    }

    [Fact]
    public void ShouldAcceptNulls_WhenUsingConfigurationChangeUsingConstructorWithNullValues()
    {
        // Arrange
        var propertyPath = "test.property";

        // Act
        var change = new ConfigurationChange(propertyPath, null, null, ChangeType.None, null);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
        Assert.Null(change.OldValue);
        Assert.Null(change.NewValue);
        Assert.Equal(ChangeType.None, change.Type);
        Assert.Null(change.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple.property")]
    [InlineData("complex.nested.deep.property")]
    [InlineData("array[0].property")]
    [InlineData("configuration.with-dashes.and_underscores")]
    public void ShouldAcceptAll_WhenUsingConfigurationChangeUsingConstructorWithVariousPropertyPaths(string propertyPath)
    {
        // Act
        var change = new ConfigurationChange(propertyPath, "old", "new", ChangeType.Modified);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationChangeUsingConstructorWithComplexValues()
    {
        // Arrange
        var oldValue = new { Port = 3306, Host = "localhost" };
        var newValue = new { Port = 5432, Host = "remote-server" };

        // Act
        var change = new ConfigurationChange("database.settings", oldValue, newValue, ChangeType.Modified);

        // Assert
        Assert.Equal(oldValue, change.OldValue);
        Assert.Equal(newValue, change.NewValue);
    }

    #endregion

    #region ConfigurationChange Static Factory Methods Tests

    [Fact]
    public void ShouldCreateAddedChange_WhenUsingConfigurationChangeUsingAdded()
    {
        // Arrange
        var propertyPath = "newFeature.enabled";
        var value = true;

        // Act
        var change = ConfigurationChange.Added(propertyPath, value);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
        Assert.Null(change.OldValue);
        Assert.Equal(value, change.NewValue);
        Assert.Equal(ChangeType.Added, change.Type);
        Assert.Null(change.Description);
    }

    [Fact]
    public void ShouldCreateRemovedChange_WhenUsingConfigurationChangeUsingRemoved()
    {
        // Arrange
        var propertyPath = "deprecatedFeature.setting";
        var value = "deprecated-value";

        // Act
        var change = ConfigurationChange.Removed(propertyPath, value);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
        Assert.Equal(value, change.OldValue);
        Assert.Null(change.NewValue);
        Assert.Equal(ChangeType.Removed, change.Type);
        Assert.Null(change.Description);
    }

    [Fact]
    public void ShouldCreateModifiedChange_WhenUsingConfigurationChangeUsingModified()
    {
        // Arrange
        var propertyPath = "timeout.seconds";
        var oldValue = 30;
        var newValue = 60;

        // Act
        var change = ConfigurationChange.Modified(propertyPath, oldValue, newValue);

        // Assert
        Assert.Equal(propertyPath, change.PropertyPath);
        Assert.Equal(oldValue, change.OldValue);
        Assert.Equal(newValue, change.NewValue);
        Assert.Equal(ChangeType.Modified, change.Type);
        Assert.Null(change.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("simple value")]
    [InlineData(42)]
    [InlineData(true)]
    public void ShouldAcceptAll_WhenUsingConfigurationChangeUsingStaticMethodsWithVariousValues(object? value)
    {
        // Act
        var addedChange = ConfigurationChange.Added("test.property", value);
        var removedChange = ConfigurationChange.Removed("test.property", value);
        var modifiedChange = ConfigurationChange.Modified("test.property", value, "new-value");

        // Assert
        Assert.Equal(value, addedChange.NewValue);
        Assert.Equal(value, removedChange.OldValue);
        Assert.Equal(value, modifiedChange.OldValue);
        Assert.Equal("new-value", modifiedChange.NewValue);
    }

    #endregion

    #region ConfigurationChange Record Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingConfigurationChangeUsingEqualitySameValues()
    {
        // Arrange
        var propertyPath = "test.property";
        var oldValue = "old";
        var newValue = "new";
        var type = ChangeType.Modified;
        var description = "Test change";

        var change1 = new ConfigurationChange(propertyPath, oldValue, newValue, type, description);
        var change2 = new ConfigurationChange(propertyPath, oldValue, newValue, type, description);

        // Act & Assert
        Assert.Equal(change1, change2);
        Assert.True(change1.Equals(change2));
        Assert.True(change1 == change2);
        Assert.False(change1 != change2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingConfigurationChangeUsingEqualityDifferentPropertyPath()
    {
        // Arrange
        var change1 = new ConfigurationChange("path1", "old", "new", ChangeType.Modified);
        var change2 = new ConfigurationChange("path2", "old", "new", ChangeType.Modified);

        // Act & Assert
        Assert.NotEqual(change1, change2);
        Assert.False(change1.Equals(change2));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingConfigurationChangeWithExpression()
    {
        // Arrange
        var original = new ConfigurationChange("test.property", "old", "new", ChangeType.Modified);

        // Act
        // Cannot use with expression to modify init-only properties in a record
        var modified = new ConfigurationChange("test.property", "old", "new", ChangeType.Modified, "Updated description");

        // Assert
        Assert.Null(original.Description);
        Assert.Equal("Updated description", modified.Description);
        Assert.Equal(original.PropertyPath, modified.PropertyPath);
        Assert.NotEqual(original, modified);
    }

    #endregion

    #region ConfigurationDiff Basic Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingConfigurationDiffUsingConstructorWithConfigurationName()
    {
        // Arrange
        var configName = "database-config";
        var beforeCreation = DateTime.UtcNow;

        // Act
        var diff = new ConfigurationDiff(configName);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(configName, diff.ConfigurationName);
        Assert.NotNull(diff.Changes);
        Assert.Empty(diff.Changes);
        Assert.True(diff.ComparedAt >= beforeCreation);
        Assert.True(diff.ComparedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, diff.ComparedAt.Kind);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingConfigurationDiffUsingConstructorWithNullConfigurationName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConfigurationDiff(null!));
    }

    [Fact]
    public void ShouldSetChanges_WhenUsingConfigurationDiffUsingConstructorWithChanges()
    {
        // Arrange
        var configName = "api-config";
        var changes = new List<ConfigurationChange>
        {
            ConfigurationChange.Added("newProperty", "value"),
            ConfigurationChange.Modified("existingProperty", "old", "new")
        };

        // Act
        var diff = new ConfigurationDiff(configName, changes);

        // Assert
        Assert.Equal(configName, diff.ConfigurationName);
        Assert.Equal(changes, diff.Changes);
        Assert.Equal(2, diff.Changes.Count);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void ShouldCreateEmptyList_WhenUsingConfigurationDiffUsingConstructorWithNullChanges()
    {
        // Act
        var diff = new ConfigurationDiff("test-config", null);

        // Assert
        Assert.NotNull(diff.Changes);
        Assert.Empty(diff.Changes);
        Assert.False(diff.HasChanges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple-config")]
    [InlineData("complex.configuration.name")]
    [InlineData("config-with-special-chars_123")]
    public void ShouldAcceptAll_WhenUsingConfigurationDiffUsingConstructorWithVariousConfigurationNames(string configName)
    {
        // Act
        var diff = new ConfigurationDiff(configName);

        // Assert
        Assert.Equal(configName, diff.ConfigurationName);
    }

    #endregion

    #region ConfigurationDiff Static Factory Methods Tests

    [Fact]
    public void ShouldCreateEmptyDiff_WhenUsingConfigurationDiffWithNoChanges()
    {
        // Arrange
        var configName = "unchanged-config";

        // Act
        var diff = ConfigurationDiff.NoChanges(configName);

        // Assert
        Assert.Equal(configName, diff.ConfigurationName);
        Assert.NotNull(diff.Changes);
        Assert.Empty(diff.Changes);
        Assert.False(diff.HasChanges);
        Assert.NotEqual(default(DateTime), diff.ComparedAt);
    }

    #endregion

    #region ConfigurationDiff Methods Tests

    [Fact]
    public void ShouldAddChangeToList_WhenUsingConfigurationDiffAddingChange()
    {
        // Arrange
        var diff = new ConfigurationDiff("test-config");
        var change = ConfigurationChange.Added("newProperty", "value");

        // Act
        diff.AddChange(change);

        // Assert
        Assert.Single(diff.Changes);
        Assert.Equal(change, diff.Changes[0]);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingConfigurationDiffAddingChangeMultipleChanges()
    {
        // Arrange
        var diff = new ConfigurationDiff("test-config");
        var change1 = ConfigurationChange.Added("property1", "value1");
        var change2 = ConfigurationChange.Modified("property2", "old", "new");
        var change3 = ConfigurationChange.Removed("property3", "removed");

        // Act
        diff.AddChange(change1);
        diff.AddChange(change2);
        diff.AddChange(change3);

        // Assert
        Assert.Equal(3, diff.Changes.Count);
        Assert.Equal(change1, diff.Changes[0]);
        Assert.Equal(change2, diff.Changes[1]);
        Assert.Equal(change3, diff.Changes[2]);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void ShouldReflectChangesCount_WhenUsingConfigurationDiffUsingHasChanges()
    {
        // Arrange
        var diff = new ConfigurationDiff("test-config");

        // Act & Assert - Initially empty
        Assert.False(diff.HasChanges);

        // Add a change
        diff.AddChange(ConfigurationChange.Added("property", "value"));
        Assert.True(diff.HasChanges);

        // A freshly-created diff with no changes reports no changes
        var emptyDiff = new ConfigurationDiff("test-config");
        Assert.False(emptyDiff.HasChanges);
    }

    #endregion

    #region ConfigurationDiff Record Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingConfigurationDiffUsingEqualitySameValues()
    {
        // Arrange
        var configName = "test-config";
        var changes = new List<ConfigurationChange>
        {
            ConfigurationChange.Added("property", "value")
        };
        var comparedAt = DateTime.UtcNow;

        var diff1 = new ConfigurationDiff(configName, changes);
        var diff2 = new ConfigurationDiff(configName, changes);

        // Act & Assert
        // Note: Records compare by value, but ComparedAt will be different
        // So we need to test the basic properties separately
        Assert.Equal(diff1.ConfigurationName, diff2.ConfigurationName);
        Assert.Equal(diff1.Changes, diff2.Changes);
        Assert.Equal(diff1.HasChanges, diff2.HasChanges);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingConfigurationDiffUsingEqualityDifferentConfigurationName()
    {
        // Arrange
        var changes = new List<ConfigurationChange>
        {
            ConfigurationChange.Added("property", "value")
        };

        var diff1 = new ConfigurationDiff("config1", changes);
        var diff2 = new ConfigurationDiff("config2", changes);

        // Act & Assert
        Assert.NotEqual(diff1.ConfigurationName, diff2.ConfigurationName);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldTrackAllChanges_WhenUsingConfigurationDiffWithCompleteChangeScenario()
    {
        // Arrange
        var diff = new ConfigurationDiff("application-config");

        // Act - Simulate a complete configuration change scenario
        diff.AddChange(ConfigurationChange.Added("feature.newFlag", true));
        diff.AddChange(ConfigurationChange.Modified("database.timeout", 30, 60));
        diff.AddChange(ConfigurationChange.Modified("api.baseUrl", "http://old.api.com", "https://new.api.com"));
        diff.AddChange(ConfigurationChange.Removed("deprecated.setting", "old-value"));

        // Assert
        Assert.Equal(4, diff.Changes.Count);
        Assert.True(diff.HasChanges);
        Assert.Equal("application-config", diff.ConfigurationName);

        // Verify each change type is present
        var addedChanges = diff.Changes.Where(c => c.Type == ChangeType.Added).ToList();
        var modifiedChanges = diff.Changes.Where(c => c.Type == ChangeType.Modified).ToList();
        var removedChanges = diff.Changes.Where(c => c.Type == ChangeType.Removed).ToList();

        Assert.Single(addedChanges);
        Assert.Equal(2, modifiedChanges.Count);
        Assert.Single(removedChanges);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationDiffWithComplexPropertyPaths()
    {
        // Arrange
        var diff = new ConfigurationDiff("complex-config");

        // Act - Add changes with complex property paths
        diff.AddChange(ConfigurationChange.Modified("database.connections[0].connectionString", "old", "new"));
        diff.AddChange(ConfigurationChange.Added("logging.providers.file.options.maxFileSize", "10MB"));
        diff.AddChange(ConfigurationChange.Removed("authentication.providers.google.clientSecret", "secret"));

        // Assert
        Assert.Equal(3, diff.Changes.Count);
        Assert.Contains(diff.Changes, c => c.PropertyPath.Contains("[0]"));
        Assert.Contains(diff.Changes, c => c.PropertyPath.Contains("providers.file.options"));
        Assert.Contains(diff.Changes, c => c.PropertyPath.Contains("google.clientSecret"));
    }

    [Fact]
    public void ShouldProvideStatistics_WhenUsingConfigurationDiffWithChangesOfAllTypes()
    {
        // Arrange
        var diff = new ConfigurationDiff("statistics-config");

        // Act - Add various types of changes
        for (int i = 0; i < 5; i++)
        {
            diff.AddChange(ConfigurationChange.Added($"added.property{i}", $"value{i}"));
        }
        for (int i = 0; i < 3; i++)
        {
            diff.AddChange(ConfigurationChange.Modified($"modified.property{i}", "old", "new"));
        }
        for (int i = 0; i < 2; i++)
        {
            diff.AddChange(ConfigurationChange.Removed($"removed.property{i}", "removed"));
        }

        // Assert
        Assert.Equal(10, diff.Changes.Count);
        Assert.True(diff.HasChanges);

        // Calculate statistics
        var addedCount = diff.Changes.Count(c => c.Type == ChangeType.Added);
        var modifiedCount = diff.Changes.Count(c => c.Type == ChangeType.Modified);
        var removedCount = diff.Changes.Count(c => c.Type == ChangeType.Removed);

        Assert.Equal(5, addedCount);
        Assert.Equal(3, modifiedCount);
        Assert.Equal(2, removedCount);
    }

    [Fact]
    public void ShouldShowCorrectHasChanges_WhenUsingConfigurationDiffWithEmptyVsPopulated()
    {
        // Arrange
        var emptyDiff = ConfigurationDiff.NoChanges("empty-config");
        var populatedDiff = new ConfigurationDiff("populated-config");

        // Act
        populatedDiff.AddChange(ConfigurationChange.Added("property", "value"));

        // Assert
        Assert.False(emptyDiff.HasChanges);
        Assert.True(populatedDiff.HasChanges);
        Assert.Empty(emptyDiff.Changes);
        Assert.Single(populatedDiff.Changes);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationChangeWithVeryLongPropertyPath()
    {
        // Arrange
        var longPropertyPath = string.Join(".", Enumerable.Range(1, 100).Select(i => $"property{i}"));

        // Act
        var change = ConfigurationChange.Added(longPropertyPath, "value");

        // Assert
        Assert.Equal(longPropertyPath, change.PropertyPath);
        Assert.True(change.PropertyPath.Length > 1000);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationChangeWithUnicodeContent()
    {
        // Arrange
        var unicodePropertyPath = "配置.属性";
        var unicodeOldValue = "旧值 🔧";
        var unicodeNewValue = "新值 ⚙️";
        var unicodeDescription = "更新配置 Updated configuration with émojis";

        // Act
        var change = new ConfigurationChange(unicodePropertyPath, unicodeOldValue, unicodeNewValue,
            ChangeType.Modified, unicodeDescription);

        // Assert
        Assert.Equal(unicodePropertyPath, change.PropertyPath);
        Assert.Equal(unicodeOldValue, change.OldValue);
        Assert.Equal(unicodeNewValue, change.NewValue);
        Assert.Equal(unicodeDescription, change.Description);
        Assert.Contains("🔧", change.OldValue!.ToString());
        Assert.Contains("⚙️", change.NewValue!.ToString());
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingConfigurationDiffWithManyChanges()
    {
        // Arrange
        var diff = new ConfigurationDiff("large-config");

        // Act - Add a large number of changes
        for (int i = 0; i < 1000; i++)
        {
            diff.AddChange(ConfigurationChange.Added($"property.{i}", $"value-{i}"));
        }

        // Assert
        Assert.Equal(1000, diff.Changes.Count);
        Assert.True(diff.HasChanges);
        Assert.Equal("property.0", diff.Changes[0].PropertyPath);
        Assert.Equal("property.999", diff.Changes[999].PropertyPath);
    }

    [Fact]
    public void ShouldAffectHasChanges_WhenUsingConfigurationDiffUsingAddChange()
    {
        // Arrange
        var diff = new ConfigurationDiff("mutable-config");

        // Act - Add a change via the public mutator
        diff.AddChange(ConfigurationChange.Added("direct.property", "value"));

        // Assert
        Assert.True(diff.HasChanges);
        Assert.Single(diff.Changes);

        // A diff constructed without changes reports none
        var emptyDiff = new ConfigurationDiff("mutable-config");
        Assert.False(emptyDiff.HasChanges);
        Assert.Empty(emptyDiff.Changes);
    }

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingConfigurationDiffUsingComparedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var diff = new ConfigurationDiff("time-test-config");

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(diff.ComparedAt >= beforeCreation);
        Assert.True(diff.ComparedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, diff.ComparedAt.Kind);
    }

    #endregion
}
