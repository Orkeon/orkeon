using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Parsers;

public class YamlOutputParserTests
{
    private readonly YamlOutputParser<TestConfig> _typedParser = new();
    private readonly YamlOutputParser _nonGenericParser = new();

    [Fact]
    public void ShouldReturnTypedObject_WhenParseValidYaml()
    {
        var yaml = """
            name: MyApp
            version: 1.0
            enabled: true
            """;
        var result = _typedParser.Parse(yaml);

        Assert.NotNull(result);
        Assert.Equal("MyApp", result.Name);
        Assert.Equal("1.0", result.Version);
        Assert.True(result.Enabled);
    }

    [Fact]
    public void ShouldReturnTypedObject_WhenParseYamlInCodeBlock()
    {
        var input = """
            Here is the configuration:
            ```yaml
            name: TestApp
            version: 2.0
            enabled: false
            ```
            Please review.
            """;
        var result = _typedParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("TestApp", result.Name);
        Assert.Equal("2.0", result.Version);
        Assert.False(result.Enabled);
    }

    [Fact]
    public void ShouldReturnTypedObject_WhenParseYamlInYmlCodeBlock()
    {
        var input = """
            ```yml
            name: AnotherApp
            version: 3.0
            enabled: true
            ```
            """;
        var result = _typedParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("AnotherApp", result.Name);
    }

    [Fact]
    public void ShouldThrowException_WhenParseInvalidYaml()
    {
        // Tabs at the start cause YamlDotNet to fail
        var invalidYaml = "\t\tkey: value\n\t\t\tinvalid: [unclosed";
        Assert.ThrowsAny<Exception>(() => _typedParser.Parse(invalidYaml));
    }

    [Fact]
    public void ShouldReturnTrueWithResult_WhenTryParseValidYaml()
    {
        var yaml = """
            name: ParseTest
            version: 4.0
            enabled: true
            """;
        var success = _typedParser.TryParse(yaml, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("ParseTest", result!.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseEmptyString()
    {
        var success = _typedParser.TryParse("", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldReturnTypedObject_WhenParseAsyncValidYaml()
    {
        var yaml = """
            name: AsyncTest
            version: 5.0
            enabled: true
            """;
        var result = await _typedParser.ParseAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("AsyncTest", result.Name);
    }

    [Fact]
    public void ShouldReturnObject_WhenNonGenericParseValidYaml()
    {
        var yaml = """
            name: NonGeneric
            version: 6.0
            enabled: true
            """;
        var result = _nonGenericParser.Parse(yaml, typeof(TestConfig));

        Assert.NotNull(result);
        var config = Assert.IsType<TestConfig>(result);
        Assert.Equal("NonGeneric", config.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenNonGenericTryParseEmptyString()
    {
        var success = _nonGenericParser.TryParse("", typeof(TestConfig), out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    public class TestConfig
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public bool Enabled { get; set; }
    }
}
