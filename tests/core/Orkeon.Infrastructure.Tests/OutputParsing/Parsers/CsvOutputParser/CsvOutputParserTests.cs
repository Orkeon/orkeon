using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Parsers;

public class CsvOutputParserTests
{
    [Fact]
    public void ShouldReturnSingleRecord_WhenParseValidCsvWithHeaders()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var csv = "Name,Score\nAlice,95";

        var result = parser.Parse(csv);

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(95, result.Score);
    }

    [Fact]
    public void ShouldReturnSingleRecord_WhenParseCsvInCodeBlock()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var input = """
            Here are the results:
            ```csv
            Name,Score
            Bob,87
            ```
            """;

        var result = parser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(87, result.Score);
    }

    [Fact]
    public void ShouldReturnFirstRecord_WhenParseMultiRowCsv()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var csv = "Name,Score\nAlice,95\nBob,87\nCarol,92";

        var result = parser.Parse(csv);

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(95, result.Score);
    }

    [Fact]
    public void ShouldReturnAllRecords_WhenParseListType()
    {
        var parser = new CsvOutputParser<List<TestRecord>>();
        var csv = "Name,Score\nAlice,95\nBob,87\nCarol,92";

        var result = parser.Parse(csv);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal("Bob", result[1].Name);
        Assert.Equal("Carol", result[2].Name);
    }

    [Fact]
    public void ShouldReturnTrueWithResult_WhenTryParseValidCsv()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var csv = "Name,Score\nDave,88";

        var success = parser.TryParse(csv, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal("Dave", result!.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenTryParseEmptyString()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var success = parser.TryParse("", out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldReturnSingleRecord_WhenParseAsyncValidCsv()
    {
        var parser = new CsvOutputParser<TestRecord>();
        var csv = "Name,Score\nEve,91";

        var result = await parser.ParseAsync(csv, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Eve", result.Name);
    }

    [Fact]
    public void ShouldReturnObject_WhenNonGenericParseValidCsv()
    {
        var parser = new CsvOutputParser();
        var csv = "Name,Score\nFrank,78";

        var result = parser.Parse(csv, typeof(TestRecord));

        Assert.NotNull(result);
        var record = Assert.IsType<TestRecord>(result);
        Assert.Equal("Frank", record.Name);
    }

    [Fact]
    public void ShouldReturnFalse_WhenNonGenericTryParseEmptyString()
    {
        var parser = new CsvOutputParser();
        var success = parser.TryParse("", typeof(TestRecord), out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    public class TestRecord
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
    }
}
