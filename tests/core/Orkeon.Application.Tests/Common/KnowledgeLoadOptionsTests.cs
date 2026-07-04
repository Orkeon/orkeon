using Orkeon.Application.Rag;

namespace Orkeon.Application.Tests.Common;

public class KnowledgeLoadOptionsTests
{
    [Fact]
    public void ShouldUseDefaultValues_WhenConstructingWithDefaults()
    {
        // Act
        var options = new KnowledgeLoadOptions();

        // Assert
        Assert.False(options.ForceReload);
        Assert.Null(options.MaxItems);
        Assert.Null(options.ModifiedAfter);
        Assert.Null(options.Filter);
    }

    [Fact]
    public void ShouldSetAllValues_WhenConstructingWithAllParameters()
    {
        // Arrange
        var forceReload = true;
        var maxItems = 100;
        var modifiedAfter = DateTime.UtcNow.AddDays(-7);
        var filter = "type:document";

        // Act
        var options = new KnowledgeLoadOptions(forceReload, maxItems, modifiedAfter, filter);

        // Assert
        Assert.True(options.ForceReload);
        Assert.Equal(100, options.MaxItems);
        Assert.Equal(modifiedAfter, options.ModifiedAfter);
        Assert.Equal("type:document", options.Filter);
    }

    [Fact]
    public void ShouldMixSpecifiedAndDefaults_WhenConstructingWithPartialParameters()
    {
        // Act - Only ForceReload
        var options1 = new KnowledgeLoadOptions(ForceReload: true);
        Assert.True(options1.ForceReload);
        Assert.Null(options1.MaxItems);
        Assert.Null(options1.ModifiedAfter);
        Assert.Null(options1.Filter);

        // Act - Only MaxItems
        var options2 = new KnowledgeLoadOptions(MaxItems: 50);
        Assert.False(options2.ForceReload);
        Assert.Equal(50, options2.MaxItems);
        Assert.Null(options2.ModifiedAfter);
        Assert.Null(options2.Filter);

        // Act - Only ModifiedAfter
        var date = DateTime.UtcNow;
        var options3 = new KnowledgeLoadOptions(ModifiedAfter: date);
        Assert.False(options3.ForceReload);
        Assert.Null(options3.MaxItems);
        Assert.Equal(date, options3.ModifiedAfter);
        Assert.Null(options3.Filter);

        // Act - Only Filter
        var options4 = new KnowledgeLoadOptions(Filter: "category:research");
        Assert.False(options4.ForceReload);
        Assert.Null(options4.MaxItems);
        Assert.Null(options4.ModifiedAfter);
        Assert.Equal("category:research", options4.Filter);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenConstructingWithNamedParameters()
    {
        // Act
        var options = new KnowledgeLoadOptions(
            Filter: "test",
            MaxItems: 25,
            ForceReload: true);

        // Assert
        Assert.True(options.ForceReload);
        Assert.Equal(25, options.MaxItems);
        Assert.Null(options.ModifiedAfter);
        Assert.Equal("test", options.Filter);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var date = DateTime.UtcNow;
        var options1 = new KnowledgeLoadOptions(true, 100, date, "filter");
        var options2 = new KnowledgeLoadOptions(true, 100, date, "filter");

        // Act & Assert
        Assert.Equal(options1, options2);
        Assert.True(options1 == options2);
        Assert.False(options1 != options2);
        Assert.Equal(options1.GetHashCode(), options2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var baseOptions = new KnowledgeLoadOptions(false, 50, DateTime.UtcNow, "base");

        // Act & Assert - Different ForceReload
        var differentForce = new KnowledgeLoadOptions(true, 50, baseOptions.ModifiedAfter, "base");
        Assert.NotEqual(baseOptions, differentForce);

        // Different MaxItems
        var differentMax = new KnowledgeLoadOptions(false, 100, baseOptions.ModifiedAfter, "base");
        Assert.NotEqual(baseOptions, differentMax);

        // Different ModifiedAfter
        var differentDate = new KnowledgeLoadOptions(false, 50, DateTime.UtcNow.AddDays(1), "base");
        Assert.NotEqual(baseOptions, differentDate);

        // Different Filter
        var differentFilter = new KnowledgeLoadOptions(false, 50, baseOptions.ModifiedAfter, "different");
        Assert.NotEqual(baseOptions, differentFilter);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenRecordingEqualityWithNullValues()
    {
        // Arrange
        var options1 = new KnowledgeLoadOptions(false, null, null, null);
        var options2 = new KnowledgeLoadOptions(false, null, null, null);
        var options3 = new KnowledgeLoadOptions(false, 10, null, null);

        // Act & Assert
        Assert.Equal(options1, options2);
        Assert.NotEqual(options1, options3);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = new KnowledgeLoadOptions(false, 50, DateTime.UtcNow, "original");

        // Act
        var modified = original with { ForceReload = true, Filter = "modified" };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.False(original.ForceReload);
        Assert.True(modified.ForceReload);
        Assert.Equal(original.MaxItems, modified.MaxItems);
        Assert.Equal(original.ModifiedAfter, modified.ModifiedAfter);
        Assert.Equal("original", original.Filter);
        Assert.Equal("modified", modified.Filter);
    }

    [Fact]
    public void ShouldWork_WhenUsingWithSyntaxSettingToNull()
    {
        // Arrange
        var original = new KnowledgeLoadOptions(true, 100, DateTime.UtcNow, "filter");

        // Act
        var modified = original with { MaxItems = null, Filter = null };

        // Assert
        Assert.Equal(100, original.MaxItems);
        Assert.Equal("filter", original.Filter);
        Assert.Null(modified.MaxItems);
        Assert.Null(modified.Filter);
        Assert.Equal(original.ForceReload, modified.ForceReload);
        Assert.Equal(original.ModifiedAfter, modified.ModifiedAfter);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var date = DateTime.UtcNow;
        var options = new KnowledgeLoadOptions(true, 75, date, "test-filter");

        // Act
        var (forceReload, maxItems, modifiedAfter, filter) = options;

        // Assert
        Assert.True(forceReload);
        Assert.Equal(75, maxItems);
        Assert.Equal(date, modifiedAfter);
        Assert.Equal("test-filter", filter);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var options = new KnowledgeLoadOptions(
            true,
            100,
            new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            "category:important");

        // Act
        var result = options.ToString();

        // Assert
        Assert.Contains("True", result);  // ForceReload
        Assert.Contains("100", result);   // MaxItems
        Assert.Contains("2024", result);  // Date part
        Assert.Contains("category:important", result); // Filter
    }

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingDefaultInstance()
    {
        // Act
        var defaultOptions = default(KnowledgeLoadOptions);

        // Assert
        // Record structs would be null, but record classes create a valid instance
        Assert.Null(defaultOptions);
    }

    [Fact]
    public void ShouldFilteringAndPagination_WhenUsingComplexScenario()
    {
        // Scenario: Load recent documents with pagination
        var options = new KnowledgeLoadOptions(
            ForceReload: false,
            MaxItems: 20,
            ModifiedAfter: DateTime.UtcNow.AddDays(-30),
            Filter: "type:document AND status:published");

        // Simulate modifying for next page
        var nextPageOptions = options with
        {
            ForceReload = true,  // Force reload for fresh data
            MaxItems = 20        // Keep same page size
        };

        // Simulate clearing filters
        var allItemsOptions = options with
        {
            Filter = null,
            MaxItems = null,
            ModifiedAfter = null
        };

        // Verify scenarios
        Assert.False(options.ForceReload);
        Assert.True(nextPageOptions.ForceReload);
        Assert.Equal(options.Filter, nextPageOptions.Filter);

        Assert.Null(allItemsOptions.Filter);
        Assert.Null(allItemsOptions.MaxItems);
        Assert.Null(allItemsOptions.ModifiedAfter);
        Assert.False(allItemsOptions.ForceReload);
    }

    [Fact]
    public void ShouldAccept_WhenUsingMaxItemsWithVariousValues()
    {
        // Act & Assert - Zero
        var zeroOptions = new KnowledgeLoadOptions(MaxItems: 0);
        Assert.Equal(0, zeroOptions.MaxItems);

        // Negative (might be used for special meaning)
        var negativeOptions = new KnowledgeLoadOptions(MaxItems: -1);
        Assert.Equal(-1, negativeOptions.MaxItems);

        // Large number
        var largeOptions = new KnowledgeLoadOptions(MaxItems: int.MaxValue);
        Assert.Equal(int.MaxValue, largeOptions.MaxItems);
    }

    [Fact]
    public void ShouldAccept_WhenUsingModifiedAfterWithVariousDates()
    {
        // Act & Assert - Min value
        var minOptions = new KnowledgeLoadOptions(ModifiedAfter: DateTime.MinValue);
        Assert.Equal(DateTime.MinValue, minOptions.ModifiedAfter);

        // Max value
        var maxOptions = new KnowledgeLoadOptions(ModifiedAfter: DateTime.MaxValue);
        Assert.Equal(DateTime.MaxValue, maxOptions.ModifiedAfter);

        // Specific timezone
        var specificDate = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Local);
        var localOptions = new KnowledgeLoadOptions(ModifiedAfter: specificDate);
        Assert.Equal(specificDate, localOptions.ModifiedAfter);
    }

    [Fact]
    public void ShouldAccept_WhenFilteringWithVariousStrings()
    {
        // Act & Assert - Empty string
        var emptyOptions = new KnowledgeLoadOptions(Filter: "");
        Assert.Equal("", emptyOptions.Filter);

        // Whitespace
        var whitespaceOptions = new KnowledgeLoadOptions(Filter: "   ");
        Assert.Equal("   ", whitespaceOptions.Filter);

        // Complex query
        var complexFilter = "type:document AND (status:published OR status:draft) AND author:'John Doe' AND created>2024-01-01";
        var complexOptions = new KnowledgeLoadOptions(Filter: complexFilter);
        Assert.Equal(complexFilter, complexOptions.Filter);
    }
}
