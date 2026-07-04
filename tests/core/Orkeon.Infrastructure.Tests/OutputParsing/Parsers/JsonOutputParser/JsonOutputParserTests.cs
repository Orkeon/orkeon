using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Parsers;

public class JsonOutputParserTests
{
    private readonly JsonOutputParser<TestPerson> _typedParser = new();
    private readonly JsonOutputParser _nonGenericParser = new();

    // --- Typed Parser Tests ---

    [Fact]
    public void ShouldReturnTypedObject_WhenParseValidJson()
    {
        var json = """{"name":"Alice","age":30}""";
        var result = _typedParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public void ShouldReturnTypedObject_WhenParseJsonInMarkdownCodeBlock()
    {
        var input = """
            Here is the result:
            ```json
            {"name":"Bob","age":25}
            ```
            That's the output.
            """;
        var result = _typedParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public void ShouldReturnTypedObject_WhenParseJsonInGenericCodeBlock()
    {
        var input = """
            Result:
            ```
            {"name":"Carol","age":40}
            ```
            """;
        var result = _typedParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Carol", result.Name);
        Assert.Equal(40, result.Age);
    }

    [Fact]
    public void ShouldExtractAndParsesJson_WhenParseJsonMixedWithText()
    {
        var input = """
            The analysis is complete. Here are the findings:
            {"name":"Dave","age":35}
            Please review the above.
            """;
        var result = _typedParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Dave", result.Name);
        Assert.Equal(35, result.Age);
    }

    [Fact]
    public void ShouldCaseInsensitivePropertyMatching_WhenParse()
    {
        var json = """{"NAME":"Eve","AGE":28}""";
        var result = _typedParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Eve", result.Name);
        Assert.Equal(28, result.Age);
    }

    [Fact]
    public void ShouldExtractCorrectly_WhenParseJsonEmbeddedInText()
    {
        var wrappedInput = """Here is the result: {"name":"Frank","age":45} -- that's all.""";
        var result = _typedParser.Parse(wrappedInput);

        Assert.NotNull(result);
        Assert.Equal("Frank", result.Name);
        Assert.Equal(45, result.Age);
    }

    [Fact]
    public void ShouldThrowException_WhenParseInvalidJson()
    {
        var invalid = "This is not JSON at all";
        Assert.Throws<System.Text.Json.JsonException>(() => _typedParser.Parse(invalid));
    }

    [Fact]
    public void ShouldReturnTrueWithResult_WhenTryParseValidJson()
    {
        var json = """{"name":"Grace","age":32}""";
        var success = _typedParser.TryParse(json, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("Grace", result!.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseInvalidJson()
    {
        var invalid = "not json";
        var success = _typedParser.TryParse(invalid, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseEmptyString()
    {
        var success = _typedParser.TryParse("", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseNullString()
    {
        var success = _typedParser.TryParse(null!, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldReturnTypedObject_WhenParseAsyncValidJson()
    {
        var json = """{"name":"Hank","age":50}""";
        var result = await _typedParser.ParseAsync(json, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Hank", result.Name);
        Assert.Equal(50, result.Age);
    }

    [Fact]
    public void ShouldJsonArrayInText_WhenParse()
    {
        var arrayParser = new JsonOutputParser<List<string>>();
        var input = """Here is the list: ["a","b","c"] done.""";
        var result = arrayParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0]);
    }

    // --- Non-Generic Parser Tests ---

    [Fact]
    public void ShouldReturnObject_WhenNonGenericParseValidJson()
    {
        var json = """{"name":"Irene","age":27}""";
        var result = _nonGenericParser.Parse(json, typeof(TestPerson));

        Assert.NotNull(result);
        var person = Assert.IsType<TestPerson>(result);
        Assert.Equal("Irene", person.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenNonGenericTryParseInvalidJson()
    {
        var invalid = "not json";
        var success = _nonGenericParser.TryParse(invalid, typeof(TestPerson), out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ShouldParseCorrectly_WhenParseJsonWithTrailingComma()
    {
        var json = """{"name":"Jack","age":33,}""";
        var result = _typedParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal("Jack", result.Name);
    }

    public class TestPerson
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
