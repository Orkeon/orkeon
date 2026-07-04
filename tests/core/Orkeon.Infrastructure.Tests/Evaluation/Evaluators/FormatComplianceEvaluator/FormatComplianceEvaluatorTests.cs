using Orkeon.Application.Evaluation;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.Tests.Evaluation.Evaluators;

public class FormatComplianceEvaluatorTests
{
    private readonly FormatComplianceEvaluatorTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldBeFormatCompliance_WhenAccessingName()
    {
        Assert.Equal("FormatCompliance", _fixture.GetEvaluator().Name);
    }

    [Fact]
    public async Task ShouldNotRequireLlm_WhenAccessingRequiresLlm()
    {
        Assert.False(_fixture.GetEvaluator().RequiresLlm);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenJsonIsValid()
    {
        var input = new EvaluationInput(
            Output: """{"name": "test", "value": 42}""",
            ExpectedFormat: OutputFormat.Json);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
        Assert.Equal("FormatCompliance", result.EvaluatorName);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenJsonIsInvalid()
    {
        var input = new EvaluationInput(
            Output: "this is not json {broken",
            ExpectedFormat: OutputFormat.Json);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
        Assert.Contains("Invalid JSON", result.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenYamlIsValid()
    {
        var yaml = """
            name: test
            value: 42
            items:
              - one
              - two
            """;
        var input = new EvaluationInput(Output: yaml, ExpectedFormat: OutputFormat.Yaml);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenXmlIsValid()
    {
        var xml = "<root><name>test</name><value>42</value></root>";
        var input = new EvaluationInput(Output: xml, ExpectedFormat: OutputFormat.Xml);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenXmlIsInvalid()
    {
        var input = new EvaluationInput(
            Output: "<root><unclosed>",
            ExpectedFormat: OutputFormat.Xml);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenCsvIsValid()
    {
        var csv = "name,value\ntest,42\nfoo,99";
        var input = new EvaluationInput(Output: csv, ExpectedFormat: OutputFormat.Csv);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenCsvHasInconsistentColumns()
    {
        var csv = "name,value\ntest,42,extra";
        var input = new EvaluationInput(Output: csv, ExpectedFormat: OutputFormat.Csv);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenMarkdownHasConstructs()
    {
        var md = "# Title\n\nSome **bold** text with a [link](http://example.com).";
        var input = new EvaluationInput(Output: md, ExpectedFormat: OutputFormat.Markdown);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenMarkdownLacksConstructs()
    {
        var input = new EvaluationInput(
            Output: "Just plain text without any markdown constructs",
            ExpectedFormat: OutputFormat.Markdown);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenFormatIsPlainText()
    {
        var input = new EvaluationInput(
            Output: "Any text is valid text format",
            ExpectedFormat: OutputFormat.Text);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenOutputIsEmpty()
    {
        var input = new EvaluationInput(Output: "", ExpectedFormat: OutputFormat.Json);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenNoFormatIsSpecified()
    {
        var input = new EvaluationInput(Output: "Some output");

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public async Task ShouldReturnScoreOne_WhenJsonArrayIsValid()
    {
        var input = new EvaluationInput(
            Output: """[1, 2, 3]""",
            ExpectedFormat: OutputFormat.Json);

        var result = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, result.Score);
    }
}
