using Orkeon.Application.Configuration;

namespace Orkeon.Application.Tests.Common;

public class ConfigurationVersionMetadataTests
{
    [Fact]
    public void ShouldCreateConfigurationVersionMetadata_WhenCreatingWithValidParameters()
    {
        // Arrange
        var id = "config-v1";
        var version = "1.0.0";
        var createdAt = DateTime.UtcNow;
        var createdBy = "user@example.com";
        var comment = "Initial configuration";
        var tags = new List<string> { "production", "stable" };
        var isActive = true;

        // Act
        var metadata = new ConfigurationVersionMetadata(
            id,
            version,
            createdAt,
            createdBy,
            comment,
            tags,
            isActive);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(id, metadata.Id);
        Assert.Equal(version, metadata.Version);
        Assert.Equal(createdAt, metadata.CreatedAt);
        Assert.Equal(createdBy, metadata.CreatedBy);
        Assert.Equal(comment, metadata.Comment);
        Assert.Equal(tags, metadata.Tags);
        Assert.True(metadata.IsActive);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenCreatingWithNullComment()
    {
        // Arrange
        var id = "config-v2";
        var version = "2.0.0";
        var createdAt = DateTime.UtcNow;
        var createdBy = "admin";
        string? comment = null;
        var tags = new List<string> { "beta" };
        var isActive = false;

        // Act
        var metadata = new ConfigurationVersionMetadata(
            id,
            version,
            createdAt,
            createdBy,
            comment,
            tags,
            isActive);

        // Assert
        Assert.NotNull(metadata);
        Assert.Null(metadata.Comment);
        Assert.False(metadata.IsActive);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenCreatingWithEmptyTags()
    {
        // Arrange
        var id = "config-v3";
        var version = "3.0.0";
        var createdAt = DateTime.UtcNow;
        var createdBy = "system";
        var comment = "Automated update";
        var tags = new List<string>();
        var isActive = true;

        // Act
        var metadata = new ConfigurationVersionMetadata(
            id,
            version,
            createdAt,
            createdBy,
            comment,
            tags,
            isActive);

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Tags);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var tags = new List<string> { "test", "development" };

        var metadata1 = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            createdAt,
            "user1",
            "Test config",
            tags,
            true);

        var metadata2 = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            createdAt,
            "user1",
            "Test config",
            tags,
            true);

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1.Equals(metadata2));
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var tags = new List<string> { "test" };

        var metadata1 = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            createdAt,
            "user1",
            "Test config",
            tags,
            true);

        var metadata2 = new ConfigurationVersionMetadata(
            "config-2",
            "1.0.0",
            createdAt,
            "user1",
            "Test config",
            tags,
            true);

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
        Assert.False(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingWithDeconstruction()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Test",
            ["tag1"],
            true);

        // Act
        var (id, version, createdAt, createdBy, comment, tags, isActive) = metadata;

        // Assert
        Assert.Equal("config-1", id);
        Assert.Equal("1.0.0", version);
        Assert.Equal("user1", createdBy);
        Assert.Equal("Test", comment);
        Assert.Single(tags);
        Assert.True(isActive);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingProperty()
    {
        // Arrange
        var original = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Original comment",
            ["tag1"],
            true);

        // Act
        var modified = original with { Comment = "Modified comment", IsActive = false };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal("config-1", modified.Id);
        Assert.Equal("1.0.0", modified.Version);
        Assert.Equal("Modified comment", modified.Comment);
        Assert.False(modified.IsActive);
        Assert.Equal("Original comment", original.Comment);
        Assert.True(original.IsActive);
    }

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Test configuration",
            ["prod", "stable"],
            true);

        // Act
        var result = metadata.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("config-1", result);
        Assert.Contains("1.0.0", result);
    }

    [Fact]
    public void ShouldPreserveTags_WhenCreatingWithMultipleTags()
    {
        // Arrange
        var tags = new List<string> { "production", "stable", "verified", "latest" };
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Multi-tagged config",
            tags,
            true);

        // Act & Assert
        Assert.Equal(4, metadata.Tags.Count);
        Assert.Contains("production", metadata.Tags);
        Assert.Contains("stable", metadata.Tags);
        Assert.Contains("verified", metadata.Tags);
        Assert.Contains("latest", metadata.Tags);
    }

    [Fact]
    public void ShouldAcceptDate_WhenCreatingWithPastCreatedAt()
    {
        // Arrange
        var pastDate = DateTime.UtcNow.AddDays(-30);
        var metadata = new ConfigurationVersionMetadata(
            "config-old",
            "0.9.0",
            pastDate,
            "user1",
            "Old configuration",
            ["archived"],
            false);

        // Act & Assert
        Assert.Equal(pastDate, metadata.CreatedAt);
        Assert.False(metadata.IsActive);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenCreatingWithEmptyId()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            string.Empty,
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Empty ID config",
            [],
            true);

        // Act & Assert
        Assert.NotNull(metadata);
        Assert.Equal(string.Empty, metadata.Id);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenCreatingWithNullTags()
    {
        // Arrange & Act
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Config with null tags",
            null!,
            true);

        // Assert
        Assert.NotNull(metadata);
        Assert.Null(metadata.Tags);
    }

    [Fact]
    public void ShouldAcceptDate_WhenCreatingWithFutureCreatedAt()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(30);
        var metadata = new ConfigurationVersionMetadata(
            "config-future",
            "2.0.0",
            futureDate,
            "scheduler",
            "Scheduled future configuration",
            ["scheduled", "future"],
            false);

        // Act & Assert
        Assert.Equal(futureDate, metadata.CreatedAt);
        Assert.Equal("scheduler", metadata.CreatedBy);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNullOther()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Test",
            [],
            true);

        // Act & Assert
        Assert.False(metadata.Equals(null));
    }

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var tags = new List<string> { "tag1", "tag2" };

        var metadata1 = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            createdAt,
            "user1",
            "Test",
            tags,
            true);

        var metadata2 = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            createdAt,
            "user1",
            "Test",
            tags,
            true);

        // Act
        var hash1 = metadata1.GetHashCode();
        var hash2 = metadata2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingWithSpecialCharactersInStrings()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-\n\t\r",
            "1.0.0-\"beta\"",
            DateTime.UtcNow,
            "user@<>&'",
            "Comment with \"quotes\" and 'apostrophes'",
            ["tag\nwith\nnewlines", "tag\twith\ttabs"],
            true);

        // Act & Assert
        Assert.NotNull(metadata);
        Assert.Contains("\n", metadata.Id);
        Assert.Contains("\"", metadata.Version);
        Assert.Contains("<>&", metadata.CreatedBy);
        Assert.Contains("quotes", metadata.Comment!);
        Assert.Equal(2, metadata.Tags.Count);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingWithMaxDateTimeValue()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-max",
            "99.99.99",
            DateTime.MaxValue,
            "system",
            "Max date configuration",
            [],
            true);

        // Act & Assert
        Assert.NotNull(metadata);
        Assert.Equal(DateTime.MaxValue, metadata.CreatedAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingWithMinDateTimeValue()
    {
        // Arrange
        var metadata = new ConfigurationVersionMetadata(
            "config-min",
            "0.0.0",
            DateTime.MinValue,
            "system",
            "Min date configuration",
            [],
            false);

        // Act & Assert
        Assert.NotNull(metadata);
        Assert.Equal(DateTime.MinValue, metadata.CreatedAt);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingWithModifyingMultipleProperties()
    {
        // Arrange
        var original = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Original",
            ["tag1"],
            true);

        // Act
        var modified = original with
        {
            Id = "config-2",
            Version = "2.0.0",
            Comment = "Modified",
            Tags = ["tag2", "tag3"],
            IsActive = false
        };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal("config-2", modified.Id);
        Assert.Equal("2.0.0", modified.Version);
        Assert.Equal("Modified", modified.Comment);
        Assert.Equal(2, modified.Tags.Count);
        Assert.False(modified.IsActive);

        // Original should remain unchanged
        Assert.Equal("config-1", original.Id);
        Assert.Equal("1.0.0", original.Version);
        Assert.True(original.IsActive);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingWithVeryLongStrings()
    {
        // Arrange
        var longString = new string('a', 10000);
        var metadata = new ConfigurationVersionMetadata(
            longString,
            longString,
            DateTime.UtcNow,
            longString,
            longString,
            [longString],
            true);

        // Act & Assert
        Assert.NotNull(metadata);
        Assert.Equal(10000, metadata.Id.Length);
        Assert.Equal(10000, metadata.Version.Length);
        Assert.Equal(10000, metadata.CreatedBy.Length);
        Assert.Equal(10000, metadata.Comment!.Length);
        Assert.Single(metadata.Tags);
        Assert.Equal(10000, metadata.Tags[0].Length);
    }

    [Fact]
    public void ShouldPreserveDuplicates_WhenCreatingWithDuplicateTags()
    {
        // Arrange
        var tags = new List<string> { "duplicate", "duplicate", "unique", "duplicate" };
        var metadata = new ConfigurationVersionMetadata(
            "config-1",
            "1.0.0",
            DateTime.UtcNow,
            "user1",
            "Config with duplicate tags",
            tags,
            true);

        // Act & Assert
        Assert.Equal(4, metadata.Tags.Count);
        Assert.Equal(3, metadata.Tags.Count(t => t == "duplicate"));
        Assert.Equal(1, metadata.Tags.Count(t => t == "unique"));
    }
}
