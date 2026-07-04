using Orkeon.Domain.Configuration;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for ChangeType following Clean Architecture principles.
/// Tests the change type enumeration for configuration change tracking.
/// </summary>
public class ChangeTypeTests
{
    #region Enum Values Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingChangeType()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            ChangeType.Added,
            ChangeType.Modified,
            ChangeType.Removed,
            ChangeType.None
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<ChangeType>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<ChangeType>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Theory]
    [InlineData(ChangeType.Added)]
    [InlineData(ChangeType.Modified)]
    [InlineData(ChangeType.Removed)]
    [InlineData(ChangeType.None)]
    public void ShouldBeValid_WhenUsingChangeTypeWithAllValues(ChangeType changeType)
    {
        // Act & Assert
        Assert.True(Enum.IsDefined<ChangeType>(changeType));
    }

    [Fact]
    public void ShouldBeAdded_WhenUsingChangeTypeWithDefaultValue()
    {
        // Act
        var defaultValue = default(ChangeType);

        // Assert
        Assert.Equal(ChangeType.Added, defaultValue);
    }

    #endregion

    #region String Conversion Tests

    [Fact]
    public void ShouldReturnExpectedStrings_WhenCallingToStringWithAllValues()
    {
        // Act & Assert
        Assert.Equal("Added", ChangeType.Added.ToString());
        Assert.Equal("Modified", ChangeType.Modified.ToString());
        Assert.Equal("Removed", ChangeType.Removed.ToString());
        Assert.Equal("None", ChangeType.None.ToString());
    }

    [Theory]
    [InlineData("Added", ChangeType.Added)]
    [InlineData("Modified", ChangeType.Modified)]
    [InlineData("Removed", ChangeType.Removed)]
    [InlineData("None", ChangeType.None)]
    public void ShouldReturnCorrectEnumValue_WhenParsingWithValidStrings(string input, ChangeType expected)
    {
        // Act
        var result = Enum.Parse<ChangeType>(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("added", ChangeType.Added)]
    [InlineData("MODIFIED", ChangeType.Modified)]
    [InlineData("removed", ChangeType.Removed)]
    [InlineData("NONE", ChangeType.None)]
    public void ShouldReturnCorrectEnumValue_WhenParsingCaseInsensitive(string input, ChangeType expected)
    {
        // Act
        var result = Enum.Parse<ChangeType>(input, ignoreCase: true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("")]
    [InlineData("Created")]
    [InlineData("Updated")]
    [InlineData("Deleted")]
    public void ShouldThrowArgumentException_WhenParsingWithInvalidStrings(string invalidInput)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Enum.Parse<ChangeType>(invalidInput));
    }

    [Theory]
    [InlineData("Added", true, ChangeType.Added)]
    [InlineData("Modified", true, ChangeType.Modified)]
    [InlineData("InvalidType", false, default(ChangeType))]
    [InlineData("", false, default(ChangeType))]
    public void ShouldReturnExpectedResults_WhenUsingTryParseWithVariousInputs(
        string input, bool expectedSuccess, ChangeType expectedValue)
    {
        // Act
        var success = Enum.TryParse<ChangeType>(input, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, result);
        }
    }

    #endregion

    #region Numeric Value Tests

    [Fact]
    public void ShouldBeConsistent_WhenUsingChangeTypeUsingNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)ChangeType.Added);
        Assert.Equal(1, (int)ChangeType.Modified);
        Assert.Equal(2, (int)ChangeType.Removed);
        Assert.Equal(3, (int)ChangeType.None);
    }

    [Theory]
    [InlineData(0, ChangeType.Added)]
    [InlineData(1, ChangeType.Modified)]
    [InlineData(2, ChangeType.Removed)]
    [InlineData(3, ChangeType.None)]
    public void ShouldReturnCorrectEnum_WhenUsingCastFromIntWithValidValues(int value, ChangeType expected)
    {
        // Act
        var result = (ChangeType)value;

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void ShouldNotThrowButNotBeDefined_WhenUsingCastFromIntWithInvalidValues(int invalidValue)
    {
        // Act
        var result = (ChangeType)invalidValue;

        // Assert
        Assert.False(Enum.IsDefined<ChangeType>(result));
    }

    #endregion

    #region Collection and Iteration Tests

    [Fact]
    public void ShouldReturnAllEnumValues_WhenGettingValues()
    {
        // Act
        var values = Enum.GetValues<ChangeType>();

        // Assert
        Assert.Equal(4, values.Length);
        Assert.Contains(ChangeType.Added, values);
        Assert.Contains(ChangeType.Modified, values);
        Assert.Contains(ChangeType.Removed, values);
        Assert.Contains(ChangeType.None, values);
    }

    [Fact]
    public void ShouldReturnAllEnumNames_WhenGettingNames()
    {
        // Act
        var names = Enum.GetNames<ChangeType>();

        // Assert
        Assert.Equal(4, names.Length);
        Assert.Contains("Added", names);
        Assert.Contains("Modified", names);
        Assert.Contains("Removed", names);
        Assert.Contains("None", names);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingChangeTypeInCollection()
    {
        // Arrange
        var changeTypes = new List<ChangeType>
        {
            ChangeType.Added,
            ChangeType.Modified,
            ChangeType.Removed,
            ChangeType.Added, // Duplicate
            ChangeType.None
        };

        // Act
        var addedChanges = changeTypes.Where(t => t == ChangeType.Added).ToList();
        var uniqueTypes = changeTypes.Distinct().ToList();
        var modifyingChanges = changeTypes.Where(t => t == ChangeType.Added || t == ChangeType.Modified || t == ChangeType.Removed).ToList();

        // Assert
        Assert.Equal(5, changeTypes.Count);
        Assert.Equal(2, addedChanges.Count);
        Assert.Equal(4, uniqueTypes.Count);
        Assert.Equal(4, modifyingChanges.Count); // Excludes None
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingChangeTypeInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<ChangeType>
        {
            // Act
            ChangeType.Added,
            ChangeType.Modified,
            ChangeType.Added, // Duplicate
            ChangeType.None
        };

        // Assert
        Assert.Equal(3, hashSet.Count);
        Assert.Contains(ChangeType.Added, hashSet);
        Assert.Contains(ChangeType.Modified, hashSet);
        Assert.Contains(ChangeType.None, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingChangeTypeInDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<ChangeType, string>
        {
            { ChangeType.Added, "New configuration added" },
            { ChangeType.Modified, "Configuration updated" },
            { ChangeType.Removed, "Configuration deleted" },
            { ChangeType.None, "No changes detected" }
        };

        // Act & Assert
        Assert.Equal(4, dictionary.Count);
        Assert.Equal("New configuration added", dictionary[ChangeType.Added]);
        Assert.Equal("Configuration updated", dictionary[ChangeType.Modified]);
        Assert.Equal("Configuration deleted", dictionary[ChangeType.Removed]);
        Assert.Equal("No changes detected", dictionary[ChangeType.None]);
        Assert.True(dictionary.ContainsKey(ChangeType.Added));
    }

    #endregion

    #region Comparison and Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var type1 = ChangeType.Added;
        var type2 = ChangeType.Added;

        // Act & Assert
        Assert.True(type1.Equals(type2));
        Assert.True(type1 == type2);
        Assert.False(type1 != type2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var type1 = ChangeType.Added;
        var type2 = ChangeType.Modified;

        // Act & Assert
        Assert.False(type1.Equals(type2));
        Assert.False(type1 == type2);
        Assert.True(type1 != type2);
    }

    [Fact]
    public void ShouldCompareByNumericValue_WhenComparing()
    {
        // Act & Assert
        Assert.True(ChangeType.Added.CompareTo(ChangeType.Modified) < 0);
        Assert.True(ChangeType.Removed.CompareTo(ChangeType.Added) > 0);
        Assert.Equal(0, ChangeType.None.CompareTo(ChangeType.None));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var type1 = ChangeType.Added;
        var type2 = ChangeType.Added;

        // Act
        var hash1 = type1.GetHashCode();
        var hash2 = type2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var type1 = ChangeType.Added;
        var type2 = ChangeType.Modified;

        // Act
        var hash1 = type1.GetHashCode();
        var hash2 = type2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Switch Statement and Pattern Matching Tests

    [Theory]
    [InlineData(ChangeType.Added, "add")]
    [InlineData(ChangeType.Modified, "modify")]
    [InlineData(ChangeType.Removed, "remove")]
    [InlineData(ChangeType.None, "no-change")]
    public void ShouldHandleCorrectly_WhenSwitchingStatementWithAllValues(ChangeType changeType, string expected)
    {
        // Act
        var result = changeType switch
        {
            ChangeType.Added => "add",
            ChangeType.Modified => "modify",
            ChangeType.Removed => "remove",
            ChangeType.None => "no-change",
            _ => "unknown"
        };

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldNotHaveUnhandledCases_WhenSwitchingExpressionWithAllCases()
    {
        // Arrange
        var allValues = Enum.GetValues<ChangeType>();

        // Act & Assert - Verify all cases are handled
        foreach (var changeType in allValues)
        {
            var result = changeType switch
            {
                ChangeType.Added => "handled",
                ChangeType.Modified => "handled",
                ChangeType.Removed => "handled",
                ChangeType.None => "handled",
                _ => "unhandled"
            };

            Assert.Equal("handled", result);
        }
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Fact]
    public void ShouldMakeSense_WhenUsingChangeTypeUsingSemanticGrouping()
    {
        // Arrange - Group types by their semantic meaning
        var modifyingChanges = new[] { ChangeType.Added, ChangeType.Modified, ChangeType.Removed };
        var nonModifyingChanges = new[] { ChangeType.None };

        // Act - Verify groupings make sense
        var allTypes = modifyingChanges.Concat(nonModifyingChanges).ToList();

        // Assert
        Assert.Equal(4, allTypes.Count);
        Assert.Equal(Enum.GetValues<ChangeType>().Length, allTypes.Count);

        // Verify no duplicates in grouping
        Assert.Equal(allTypes.Count, allTypes.Distinct().Count());
    }

    [Theory]
    [InlineData(ChangeType.Added, true)]
    [InlineData(ChangeType.Modified, true)]
    [InlineData(ChangeType.Removed, true)]
    [InlineData(ChangeType.None, false)]
    public void ShouldIdentifyRealChanges_WhenUsingIsActualChange(ChangeType changeType, bool expectedIsActualChange)
    {
        // Act - Define what constitutes an actual change
        var isActualChange = changeType != ChangeType.None;

        // Assert
        Assert.Equal(expectedIsActualChange, isActualChange);
    }

    [Theory]
    [InlineData(ChangeType.Added, true)]
    [InlineData(ChangeType.Modified, false)]
    [InlineData(ChangeType.Removed, false)]
    [InlineData(ChangeType.None, false)]
    public void ShouldIdentifyAdditions_WhenUsingIsAddition(ChangeType changeType, bool expectedIsAddition)
    {
        // Act - Define what constitutes an addition
        var isAddition = changeType == ChangeType.Added;

        // Assert
        Assert.Equal(expectedIsAddition, isAddition);
    }

    [Theory]
    [InlineData(ChangeType.Added, false)]
    [InlineData(ChangeType.Modified, false)]
    [InlineData(ChangeType.Removed, true)]
    [InlineData(ChangeType.None, false)]
    public void ShouldIdentifyDeletions_WhenUsingIsDeletion(ChangeType changeType, bool expectedIsDeletion)
    {
        // Act - Define what constitutes a deletion
        var isDeletion = changeType == ChangeType.Removed;

        // Assert
        Assert.Equal(expectedIsDeletion, isDeletion);
    }

    [Theory]
    [InlineData(ChangeType.Added, ChangeType.Removed)]
    [InlineData(ChangeType.Removed, ChangeType.Added)]
    [InlineData(ChangeType.Modified, ChangeType.Modified)]
    [InlineData(ChangeType.None, ChangeType.None)]
    public void ShouldReturnLogicalOpposite_WhenUsingGetOppositeChange(ChangeType input, ChangeType expectedOpposite)
    {
        // Act - Define logical opposites
        var opposite = input switch
        {
            ChangeType.Added => ChangeType.Removed,
            ChangeType.Removed => ChangeType.Added,
            ChangeType.Modified => ChangeType.Modified, // Modification is its own opposite
            ChangeType.None => ChangeType.None,
            _ => input
        };

        // Assert
        Assert.Equal(expectedOpposite, opposite);
    }

    #endregion

    #region Edge Cases and Invalid Operations

    [Fact]
    public void ShouldStillHaveValue_WhenUsingChangeTypeWithInvalidCast()
    {
        // Act
        var invalidEnum = (ChangeType)999;

        // Assert
        Assert.Equal(999, (int)invalidEnum);
        Assert.False(Enum.IsDefined<ChangeType>(invalidEnum));

        // ToString should still work
        Assert.Equal("999", invalidEnum.ToString());
    }

    [Fact]
    public void ShouldBeUniqueIntegers_WhenUsingChangeTypeWithAllValues()
    {
        // Act
        var values = Enum.GetValues<ChangeType>();
        var intValues = values.Select(v => (int)v).ToList();

        // Assert
        Assert.Equal(values.Length, intValues.Distinct().Count());

        // Should be sequential starting from 0
        var expectedValues = Enumerable.Range(0, values.Length).ToList();
        Assert.Equal(expectedValues, intValues.OrderBy(x => x).ToList());
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingChangeTypeWithNullableEnum()
    {
        // Arrange
        ChangeType? nullableEnum = null;
        ChangeType? nonNullEnum = ChangeType.Added;

        // Act & Assert
        Assert.Null(nullableEnum);
        Assert.NotNull(nonNullEnum);
        Assert.Equal(ChangeType.Added, nonNullEnum.Value);

        // Default value handling
        var defaultValue = nullableEnum ?? ChangeType.None;
        Assert.Equal(ChangeType.None, defaultValue);
    }

    #endregion
}
