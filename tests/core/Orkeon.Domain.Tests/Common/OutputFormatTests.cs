using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for OutputFormat enum following Clean Architecture principles.
/// Tests the business rules and validation logic of the OutputFormat enumeration.
/// </summary>
public class OutputFormatTests
{
    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingOutputFormat()
    {
        // Act & Assert
        Assert.Equal(0, (int)OutputFormat.Text);
        Assert.Equal(1, (int)OutputFormat.Json);
        Assert.Equal(2, (int)OutputFormat.Markdown);
        Assert.Equal(3, (int)OutputFormat.Yaml);
        Assert.Equal(4, (int)OutputFormat.Csv);
        Assert.Equal(5, (int)OutputFormat.Xml);
        Assert.Equal(6, (int)OutputFormat.Custom);
    }

    [Theory]
    [InlineData(OutputFormat.Text, "Text")]
    [InlineData(OutputFormat.Json, "Json")]
    [InlineData(OutputFormat.Markdown, "Markdown")]
    [InlineData(OutputFormat.Yaml, "Yaml")]
    [InlineData(OutputFormat.Csv, "Csv")]
    [InlineData(OutputFormat.Xml, "Xml")]
    [InlineData(OutputFormat.Custom, "Custom")]
    public void ShouldReturnCorrectName_WhenUsingOutputFormatToString(OutputFormat format, string expectedName)
    {
        // Act
        var name = format.ToString();

        // Assert
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void ShouldHaveAllExpectedFormats_WhenUsingOutputFormat()
    {
        // Arrange
        var expectedFormats = new[]
        {
            OutputFormat.Text,
            OutputFormat.Json,
            OutputFormat.Markdown,
            OutputFormat.Yaml,
            OutputFormat.Csv,
            OutputFormat.Xml,
            OutputFormat.Custom
        };

        // Act
        var allFormats = Enum.GetValues<OutputFormat>();

        // Assert
        Assert.Equal(expectedFormats.Length, allFormats.Length);
        foreach (var expectedFormat in expectedFormats)
        {
            Assert.Contains(expectedFormat, allFormats);
        }
    }

    [Theory]
    [InlineData("Text", OutputFormat.Text)]
    [InlineData("Json", OutputFormat.Json)]
    [InlineData("Markdown", OutputFormat.Markdown)]
    [InlineData("Yaml", OutputFormat.Yaml)]
    [InlineData("Csv", OutputFormat.Csv)]
    [InlineData("Xml", OutputFormat.Xml)]
    [InlineData("Custom", OutputFormat.Custom)]
    public void ShouldReturnCorrectFormat_WhenParsingWithValidString(string formatString, OutputFormat expectedFormat)
    {
        // Act
        var result = Enum.Parse<OutputFormat>(formatString);

        // Assert
        Assert.Equal(expectedFormat, result);
    }

    [Theory]
    [InlineData("text", true)]
    [InlineData("JSON", true)]
    [InlineData("markdown", true)]
    [InlineData("YAML", true)]
    [InlineData("csv", true)]
    [InlineData("XML", true)]
    [InlineData("custom", true)]
    public void ShouldWork_WhenParsingWithIgnoreCase(string formatString, bool ignoreCase)
    {
        // Act
        var result = Enum.Parse<OutputFormat>(formatString, ignoreCase);

        // Assert
        // result is value type OutputFormat, no need for NotNull check
        Assert.IsType<OutputFormat>(result);
    }

    [Theory]
    [InlineData("InvalidFormat")]
    [InlineData("")]
    [InlineData("  ")]
    public void ShouldThrowArgumentException_WhenParsingWithInvalidString(string invalidFormat)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Enum.Parse<OutputFormat>(invalidFormat));
    }

    [Theory]
    [InlineData("Text", true)]
    [InlineData("Json", true)]
    [InlineData("Markdown", true)]
    [InlineData("Yaml", true)]
    [InlineData("Csv", true)]
    [InlineData("Xml", true)]
    [InlineData("Custom", true)]
    [InlineData("Invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldReturnExpectedResult_WhenUsingTryParse(string? formatString, bool expectedSuccess)
    {
        // Act
        var success = Enum.TryParse<OutputFormat>(formatString, out var result);

        // Assert
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            // result is value type OutputFormat, no need for NotNull check
        }
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsDefinedWithValidValues()
    {
        // Arrange
        var validValues = Enum.GetValues<OutputFormat>();

        // Act & Assert
        foreach (var value in validValues)
        {
            Assert.True(Enum.IsDefined<OutputFormat>(value));
        }
    }

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(7)]
    public void ShouldReturnFalse_WhenUsingIsDefinedWithInvalidValues(int invalidValue)
    {
        // Act
        var isDefined = Enum.IsDefined(typeof(OutputFormat), invalidValue);

        // Assert
        Assert.False(isDefined);
    }

    [Fact]
    public void ShouldBeUsableInSwitch_WhenUsingOutputFormat()
    {
        // Arrange
        var format = OutputFormat.Json;

        // Act
        var description = format switch
        {
            OutputFormat.Text => "Plain text",
            OutputFormat.Json => "JSON format",
            OutputFormat.Markdown => "Markdown format",
            OutputFormat.Yaml => "YAML format",
            OutputFormat.Csv => "CSV format",
            OutputFormat.Xml => "XML format",
            OutputFormat.Custom => "Custom format",
            _ => "Unknown format"
        };

        // Assert
        Assert.Equal("JSON format", description);
    }

    [Fact]
    public void ShouldReturnAllFormatNames_WhenGettingNames()
    {
        // Act
        var names = Enum.GetNames<OutputFormat>();

        // Assert
        Assert.Equal(7, names.Length);
        Assert.Contains("Text", names);
        Assert.Contains("Json", names);
        Assert.Contains("Markdown", names);
        Assert.Contains("Yaml", names);
        Assert.Contains("Csv", names);
        Assert.Contains("Xml", names);
        Assert.Contains("Custom", names);
    }

    [Fact]
    public void ShouldBeComparable_WhenUsingOutputFormat()
    {
        // Arrange
        var format1 = OutputFormat.Text;
        var format2 = OutputFormat.Json;
        var format3 = OutputFormat.Text;

        // Act & Assert
        Assert.True(format1 < format2);
        Assert.True(format2 > format1);
        Assert.True(format1 == format3);
        Assert.True(format1 != format2);
    }

    [Fact]
    public void ShouldWorkWithHashSet_WhenUsingOutputFormat()
    {
        // Arrange
        var formats = new HashSet<OutputFormat>
        {
            // Act
            OutputFormat.Text,
            OutputFormat.Json,
            OutputFormat.Text // Duplicate
        };

        // Assert
        Assert.Equal(2, formats.Count);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
    }
}
