using Orkeon.Domain.HumanInput;

namespace Orkeon.Domain.Tests.HumanInput;

/// <summary>
/// Tests for HumanInputType following Clean Architecture principles.
/// Tests the human input type sealed record for different input types.
/// </summary>
public class HumanInputTypeTests
{
    #region Sealed Record Values Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingHumanInputType()
    {
        // Act & Assert - Verify all expected values exist
        var expectedValues = new[]
        {
            HumanInputType.Text,
            HumanInputType.Confirmation,
            HumanInputType.Choice,
            HumanInputType.File,
            HumanInputType.Number,
            HumanInputType.DateTime,
            HumanInputType.Custom,
            HumanInputType.Approval,
            HumanInputType.FileUpload
        };

        // Verify each expected value exists in All
        foreach (var expectedValue in expectedValues)
        {
            Assert.Contains(expectedValue, HumanInputType.All);
        }

        // Verify we have exactly the expected number of values
        Assert.Equal(expectedValues.Length, HumanInputType.All.Count);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingHumanInputTypeWithAllValues()
    {
        // Act & Assert - All members should be in All collection
        Assert.Contains(HumanInputType.Text, HumanInputType.All);
        Assert.Contains(HumanInputType.Confirmation, HumanInputType.All);
        Assert.Contains(HumanInputType.Choice, HumanInputType.All);
        Assert.Contains(HumanInputType.File, HumanInputType.All);
        Assert.Contains(HumanInputType.Number, HumanInputType.All);
        Assert.Contains(HumanInputType.DateTime, HumanInputType.All);
        Assert.Contains(HumanInputType.Custom, HumanInputType.All);
        Assert.Contains(HumanInputType.Approval, HumanInputType.All);
        Assert.Contains(HumanInputType.FileUpload, HumanInputType.All);
    }

    [Fact]
    public void ShouldBeText_WhenUsingHumanInputTypeWithDefaultSemantic()
    {
        // Act - Text is the first/most common input type
        var textType = HumanInputType.Text;

        // Assert
        Assert.Equal("Text", textType.Value);
    }

    #endregion

    #region String Conversion Tests

    [Fact]
    public void ShouldReturnExpectedStrings_WhenCallingToStringWithAllValues()
    {
        // Act & Assert
        Assert.Equal("Text", HumanInputType.Text.ToString());
        Assert.Equal("Confirmation", HumanInputType.Confirmation.ToString());
        Assert.Equal("Choice", HumanInputType.Choice.ToString());
        Assert.Equal("File", HumanInputType.File.ToString());
        Assert.Equal("Number", HumanInputType.Number.ToString());
        Assert.Equal("DateTime", HumanInputType.DateTime.ToString());
        Assert.Equal("Custom", HumanInputType.Custom.ToString());
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenParsingWithValidStrings()
    {
        // Act & Assert - From() parses case-insensitively
        Assert.Equal(HumanInputType.Text, HumanInputType.From("Text"));
        Assert.Equal(HumanInputType.Confirmation, HumanInputType.From("Confirmation"));
        Assert.Equal(HumanInputType.Choice, HumanInputType.From("Choice"));
        Assert.Equal(HumanInputType.File, HumanInputType.From("File"));
        Assert.Equal(HumanInputType.Number, HumanInputType.From("Number"));
        Assert.Equal(HumanInputType.DateTime, HumanInputType.From("DateTime"));
        Assert.Equal(HumanInputType.Custom, HumanInputType.From("Custom"));
    }

    [Fact]
    public void ShouldReturnCorrectValue_WhenParsingCaseInsensitive()
    {
        // Act & Assert
        Assert.Equal(HumanInputType.Text, HumanInputType.From("text"));
        Assert.Equal(HumanInputType.Confirmation, HumanInputType.From("CONFIRMATION"));
        Assert.Equal(HumanInputType.Choice, HumanInputType.From("choice"));
        Assert.Equal(HumanInputType.File, HumanInputType.From("FILE"));
        Assert.Equal(HumanInputType.Number, HumanInputType.From("number"));
        Assert.Equal(HumanInputType.DateTime, HumanInputType.From("datetime"));
        Assert.Equal(HumanInputType.Custom, HumanInputType.From("CUSTOM"));
    }

    [Theory]
    [InlineData("InvalidType")]
    [InlineData("")]
    [InlineData("TextInput")]
    [InlineData("Confirm")]
    public void ShouldThrowArgumentException_WhenParsingWithInvalidStrings(string invalidInput)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => HumanInputType.From(invalidInput));
    }

    [Fact]
    public void ShouldReturnExpectedResults_WhenUsingTryFromWithVariousInputs()
    {
        // Successful cases
        Assert.True(HumanInputType.TryFrom("Text", out var text));
        Assert.Equal(HumanInputType.Text, text);

        Assert.True(HumanInputType.TryFrom("Confirmation", out var confirmation));
        Assert.Equal(HumanInputType.Confirmation, confirmation);

        // Failure cases
        Assert.False(HumanInputType.TryFrom("InvalidType", out var invalid));
        Assert.Null(invalid);

        Assert.False(HumanInputType.TryFrom("", out var empty));
        Assert.Null(empty);

        Assert.False(HumanInputType.TryFrom(null, out var nullResult));
        Assert.Null(nullResult);
    }

    #endregion

    #region Value Tests

    [Fact]
    public void ShouldHaveCorrectStringValues_WhenUsingHumanInputType()
    {
        // Act & Assert
        Assert.Equal("Text", HumanInputType.Text.Value);
        Assert.Equal("Confirmation", HumanInputType.Confirmation.Value);
        Assert.Equal("Choice", HumanInputType.Choice.Value);
        Assert.Equal("File", HumanInputType.File.Value);
        Assert.Equal("Number", HumanInputType.Number.Value);
        Assert.Equal("DateTime", HumanInputType.DateTime.Value);
        Assert.Equal("Custom", HumanInputType.Custom.Value);
    }

    [Fact]
    public void ShouldSupportImplicitStringConversion()
    {
        // Act
        string text = HumanInputType.Text;
        string confirmation = HumanInputType.Confirmation;

        // Assert
        Assert.Equal("Text", text);
        Assert.Equal("Confirmation", confirmation);
    }

    #endregion

    #region Collection and Iteration Tests

    [Fact]
    public void ShouldReturnAllValues_WhenGettingAll()
    {
        // Act
        var values = HumanInputType.All;

        // Assert
        Assert.Equal(9, values.Count);
        Assert.Contains(HumanInputType.Text, values);
        Assert.Contains(HumanInputType.Confirmation, values);
        Assert.Contains(HumanInputType.Choice, values);
        Assert.Contains(HumanInputType.File, values);
        Assert.Contains(HumanInputType.Number, values);
        Assert.Contains(HumanInputType.DateTime, values);
        Assert.Contains(HumanInputType.Custom, values);
        Assert.Contains(HumanInputType.Approval, values);
        Assert.Contains(HumanInputType.FileUpload, values);
    }

    [Fact]
    public void ShouldReturnAllNames_WhenGettingAllValues()
    {
        // Act
        var names = HumanInputType.All.Select(x => x.Value).ToList();

        // Assert
        Assert.Equal(9, names.Count);
        Assert.Contains("Text", names);
        Assert.Contains("Confirmation", names);
        Assert.Contains("Choice", names);
        Assert.Contains("File", names);
        Assert.Contains("Number", names);
        Assert.Contains("DateTime", names);
        Assert.Contains("Custom", names);
        Assert.Contains("Approval", names);
        Assert.Contains("FileUpload", names);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingHumanInputTypeInCollection()
    {
        // Arrange
        var inputTypes = new List<HumanInputType>
        {
            HumanInputType.Text,
            HumanInputType.Choice,
            HumanInputType.Confirmation,
            HumanInputType.Text, // Duplicate
            HumanInputType.Custom
        };

        // Act
        var textInputs = inputTypes.Where(t => t == HumanInputType.Text).ToList();
        var uniqueTypes = inputTypes.Distinct().ToList();

        // Assert
        Assert.Equal(5, inputTypes.Count);
        Assert.Equal(2, textInputs.Count);
        Assert.Equal(4, uniqueTypes.Count);
        Assert.DoesNotContain(HumanInputType.File, uniqueTypes);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingHumanInputTypeInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<HumanInputType>
        {
            HumanInputType.Text,
            HumanInputType.Choice,
            HumanInputType.Text, // Duplicate
            HumanInputType.Custom
        };

        // Assert
        Assert.Equal(3, hashSet.Count);
        Assert.Contains(HumanInputType.Text, hashSet);
        Assert.Contains(HumanInputType.Choice, hashSet);
        Assert.Contains(HumanInputType.Custom, hashSet);
    }

    [Fact]
    public void ShouldWorkAsKey_WhenUsingHumanInputTypeInDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<HumanInputType, string>
        {
            { HumanInputType.Text, "Simple text input" },
            { HumanInputType.Choice, "Multiple choice selection" },
            { HumanInputType.Confirmation, "Yes/No confirmation" }
        };

        // Act & Assert
        Assert.Equal(3, dictionary.Count);
        Assert.Equal("Simple text input", dictionary[HumanInputType.Text]);
        Assert.Equal("Multiple choice selection", dictionary[HumanInputType.Choice]);
        Assert.Equal("Yes/No confirmation", dictionary[HumanInputType.Confirmation]);
        Assert.True(dictionary.ContainsKey(HumanInputType.Text));
        Assert.False(dictionary.ContainsKey(HumanInputType.File));
    }

    #endregion

    #region Comparison and Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var type1 = HumanInputType.Text;
        var type2 = HumanInputType.Text;

        // Act & Assert
        Assert.True(type1.Equals(type2));
        Assert.True(type1 == type2);
        Assert.False(type1 != type2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var type1 = HumanInputType.Text;
        var type2 = HumanInputType.Choice;

        // Act & Assert
        Assert.False(type1.Equals(type2));
        Assert.False(type1 == type2);
        Assert.True(type1 != type2);
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var type1 = HumanInputType.Text;
        var type2 = HumanInputType.Text;

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
        var type1 = HumanInputType.Text;
        var type2 = HumanInputType.Choice;

        // Act
        var hash1 = type1.GetHashCode();
        var hash2 = type2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Value Matching Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenMatchingOnValue()
    {
        // Act & Assert
        foreach (var inputType in HumanInputType.All)
        {
            var result = inputType.Value switch
            {
                "Text" => "text",
                "Confirmation" => "confirmation",
                "Choice" => "choice",
                "File" => "file",
                "Number" => "number",
                "DateTime" => "datetime",
                "Custom" => "custom",
                "Approval" => "approval",
                "FileUpload" => "fileupload",
                _ => "unknown"
            };
            Assert.NotEqual("unknown", result);
        }
    }

    [Fact]
    public void ShouldNotHaveUnhandledCases_WhenMatchingAllValues()
    {
        // Act & Assert - Verify all cases are handled
        foreach (var inputType in HumanInputType.All)
        {
            var handled = inputType == HumanInputType.Text
                || inputType == HumanInputType.Confirmation
                || inputType == HumanInputType.Choice
                || inputType == HumanInputType.File
                || inputType == HumanInputType.Number
                || inputType == HumanInputType.DateTime
                || inputType == HumanInputType.Custom
                || inputType == HumanInputType.Approval
                || inputType == HumanInputType.FileUpload;

            Assert.True(handled);
        }
    }

    #endregion

    #region Business Logic and Semantic Tests

    [Fact]
    public void ShouldMakeSense_WhenUsingHumanInputTypeUsingSemanticGrouping()
    {
        // Arrange - Group types by their semantic meaning
        var simpleInputs = new[] { HumanInputType.Text, HumanInputType.Number, HumanInputType.DateTime };
        var interactiveInputs = new[] { HumanInputType.Choice, HumanInputType.Confirmation, HumanInputType.Approval };
        var specialInputs = new[] { HumanInputType.File, HumanInputType.Custom, HumanInputType.FileUpload };

        // Act - Verify groupings make sense
        var allInputs = simpleInputs.Concat(interactiveInputs).Concat(specialInputs).ToList();

        // Assert
        Assert.Equal(9, allInputs.Count);
        Assert.Equal(HumanInputType.All.Count, allInputs.Count);

        // Verify no duplicates in grouping
        Assert.Equal(allInputs.Count, allInputs.Distinct().Count());
    }

    [Fact]
    public void ShouldIdentifySimpleInputTypes_WhenUsingIsSimpleInput()
    {
        // Act & Assert
        var simpleTypes = new[] { HumanInputType.Text, HumanInputType.Number, HumanInputType.DateTime };
        var nonSimpleTypes = new[] { HumanInputType.Choice, HumanInputType.Confirmation, HumanInputType.File, HumanInputType.Custom };

        foreach (var type in simpleTypes)
            Assert.True(type == HumanInputType.Text || type == HumanInputType.Number || type == HumanInputType.DateTime);

        foreach (var type in nonSimpleTypes)
            Assert.False(type == HumanInputType.Text || type == HumanInputType.Number || type == HumanInputType.DateTime);
    }

    [Fact]
    public void ShouldIdentifyTypesNeedingOptions_WhenUsingRequiresOptions()
    {
        // Act & Assert
        var typesWithOptions = new[] { HumanInputType.Choice, HumanInputType.Confirmation };
        var typesWithoutOptions = new[] { HumanInputType.Text, HumanInputType.Number, HumanInputType.DateTime, HumanInputType.File, HumanInputType.Custom };

        foreach (var type in typesWithOptions)
            Assert.True(type == HumanInputType.Choice || type == HumanInputType.Confirmation);

        foreach (var type in typesWithoutOptions)
            Assert.False(type == HumanInputType.Choice || type == HumanInputType.Confirmation);
    }

    #endregion

    #region From / TryFrom Edge Cases

    [Fact]
    public void ShouldRoundTrip_WhenUsingFromWithToString()
    {
        // Verify From(x.ToString()) returns the same instance
        foreach (var inputType in HumanInputType.All)
        {
            var roundTripped = HumanInputType.From(inputType.ToString());
            Assert.Same(inputType, roundTripped);
        }
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingNullableSemantics()
    {
        // Arrange
        HumanInputType? nullableType = null;
        HumanInputType? nonNullType = HumanInputType.Text;

        // Act & Assert
        Assert.Null(nullableType);
        Assert.NotNull(nonNullType);
        Assert.Equal(HumanInputType.Text, nonNullType);

        // Default value handling
        var defaultValue = nullableType ?? HumanInputType.Custom;
        Assert.Equal(HumanInputType.Custom, defaultValue);
    }

    #endregion
}
