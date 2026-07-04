using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Parsers;

public class KeyValueOutputParserTests
{
    [Fact]
    public void ShouldReturnDictionary_WhenParseColonSeparator()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = """
            Name: Alice
            Age: 30
            City: Paris
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Alice", result["Name"]);
        Assert.Equal("30", result["Age"]);
        Assert.Equal("Paris", result["City"]);
    }

    [Fact]
    public void ShouldReturnDictionary_WhenParseEqualsSeparator()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = """
            Name = Bob
            Age = 25
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Bob", result["Name"]);
        Assert.Equal("25", result["Age"]);
    }

    [Fact]
    public void ShouldReturnDictionary_WhenParseArrowSeparator()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = """
            Name -> Carol
            Age -> 40
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Carol", result["Name"]);
        Assert.Equal("40", result["Age"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenParseMultilineValue()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = """
            Summary: This is a long description
            that spans multiple lines
            until the next key.
            Status: Complete
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Contains("long description", result["Summary"]);
        Assert.Contains("multiple lines", result["Summary"]);
        Assert.Equal("Complete", result["Status"]);
    }

    [Fact]
    public void ShouldSetProperties_WhenParseMapsToObject()
    {
        var parser = new KeyValueOutputParser<TestItem>();
        var input = """
            Name: Widget
            Price: 19.99
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
    }

    [Fact]
    public void ShouldReturnTrueWithResult_WhenTryParseValidInput()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = "Key: Value";

        var success = parser.TryParse(input, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("Value", result!["Key"]);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseEmptyString()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var success = parser.TryParse("", out var result);

        Assert.False(success);
    }

    [Fact]
    public void ShouldOverwriteProperly_WhenParseCaseInsensitiveKeys()
    {
        var parser = new KeyValueOutputParser<Dictionary<string, string>>();
        var input = """
            name: Alice
            Name: Bob
            """;

        var result = parser.Parse(input);

        // Case-insensitive dictionary, so second entry overwrites
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Bob", result["name"]);
    }

    // --- Non-Generic Parser Tests ---

    [Fact]
    public void ShouldReturnDictionary_WhenNonGenericParse()
    {
        var parser = new KeyValueOutputParser();
        var input = "Key: Value\nOther: Data";

        var result = parser.Parse(input, typeof(Dictionary<string, string>));

        Assert.NotNull(result);
        var dict = Assert.IsType<Dictionary<string, string>>(result);
        Assert.Equal("Value", dict["Key"]);
        Assert.Equal("Data", dict["Other"]);
    }

    [Fact]
    public void ShouldReturnFalse_WhenNonGenericTryParseEmptyString()
    {
        var parser = new KeyValueOutputParser();
        var success = parser.TryParse("", typeof(Dictionary<string, string>), out var result);

        Assert.False(success);
    }

    public class TestItem
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }
}
