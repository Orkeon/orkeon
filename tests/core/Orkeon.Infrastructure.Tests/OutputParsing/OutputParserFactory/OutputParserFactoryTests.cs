using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing;
using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.OutputParsing;

public class OutputParserFactoryTests
{
    private readonly OutputParserFactory _factory = new();

    // --- Non-Generic CreateParser Tests ---

    [Fact]
    public void ShouldReturnJsonOutputParser_WhenCreateParserJsonFormat()
    {
        var parser = _factory.CreateParser(OutputFormat.Json);
        Assert.IsType<JsonOutputParser>(parser);
    }

    [Fact]
    public void ShouldReturnYamlOutputParser_WhenCreateParserYamlFormat()
    {
        var parser = _factory.CreateParser(OutputFormat.Yaml);
        Assert.IsType<YamlOutputParser>(parser);
    }

    [Fact]
    public void ShouldReturnCsvOutputParser_WhenCreateParserCsvFormat()
    {
        var parser = _factory.CreateParser(OutputFormat.Csv);
        Assert.IsType<CsvOutputParser>(parser);
    }

    [Fact]
    public void ShouldReturnKeyValueOutputParser_WhenCreateParserTextFormat()
    {
        var parser = _factory.CreateParser(OutputFormat.Text);
        Assert.IsType<KeyValueOutputParser>(parser);
    }

    [Fact]
    public void ShouldReturnKeyValueOutputParser_WhenCreateParserKeyValueFormat()
    {
        var parser = _factory.CreateParser(OutputFormat.KeyValue);
        Assert.IsType<KeyValueOutputParser>(parser);
    }

    [Fact]
    public void ShouldThrowNotSupportedException_WhenCreateParserUnsupportedFormat()
    {
        Assert.Throws<NotSupportedException>(() =>
            _factory.CreateParser(OutputFormat.Custom));
    }

    // --- Generic CreateParser<T> Tests ---

    [Fact]
    public void ShouldReturnJsonOutputParserT_WhenCreateGenericParserJsonFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Json);
        Assert.IsType<JsonOutputParser<TestDto>>(parser);
    }

    [Fact]
    public void ShouldReturnYamlOutputParserT_WhenCreateGenericParserYamlFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Yaml);
        Assert.IsType<YamlOutputParser<TestDto>>(parser);
    }

    [Fact]
    public void ShouldReturnCsvOutputParserT_WhenCreateGenericParserCsvFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Csv);
        Assert.IsType<CsvOutputParser<TestDto>>(parser);
    }

    [Fact]
    public void ShouldReturnKeyValueOutputParserT_WhenCreateGenericParserTextFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Text);
        Assert.IsType<KeyValueOutputParser<TestDto>>(parser);
    }

    [Fact]
    public void ShouldThrowNotSupportedException_WhenCreateGenericParserUnsupportedFormat()
    {
        Assert.Throws<NotSupportedException>(() =>
            _factory.CreateParser<TestDto>(OutputFormat.Custom));
    }

    // --- Integration: End-to-End Parsing ---

    [Fact]
    public void ShouldBeAbleToParseJson_WhenCreateParserJsonFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Json);
        var result = parser.Parse("""{"title":"Test","count":42}""");

        Assert.NotNull(result);
        Assert.Equal("Test", result.Title);
        Assert.Equal(42, result.Count);
    }

    [Fact]
    public void ShouldBeAbleToParseYaml_WhenCreateParserYamlFormat()
    {
        var parser = _factory.CreateParser<TestDto>(OutputFormat.Yaml);
        var result = parser.Parse("title: TestYaml\ncount: 7");

        Assert.NotNull(result);
        Assert.Equal("TestYaml", result.Title);
        Assert.Equal(7, result.Count);
    }

    public class TestDto
    {
        public string Title { get; set; } = "";
        public int Count { get; set; }
    }
}
