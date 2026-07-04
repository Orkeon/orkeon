using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class FormatValidatorTests
{
    private readonly FormatValidator _validator = new();

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncValidJson()
    {
        var context = new OutputValidationContext(OutputFormat.Json);
        var result = await _validator.ValidateAsync("""{"key":"value"}""", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncInvalidJson()
    {
        var context = new OutputValidationContext(OutputFormat.Json);
        var result = await _validator.ValidateAsync("not json at all", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Invalid JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncJsonInCodeBlock()
    {
        var context = new OutputValidationContext(OutputFormat.Json);
        var input = "```json\n{\"key\":\"value\"}\n```";
        var result = await _validator.ValidateAsync(input, context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncValidYaml()
    {
        var context = new OutputValidationContext(OutputFormat.Yaml);
        var result = await _validator.ValidateAsync("key: value\nother: data", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncValidCsv()
    {
        var context = new OutputValidationContext(OutputFormat.Csv);
        var csv = "Name,Age\nAlice,30\nBob,25";
        var result = await _validator.ValidateAsync(csv, context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncCsvWithMismatchedColumns()
    {
        var context = new OutputValidationContext(OutputFormat.Csv);
        var csv = "Name,Age\nAlice,30,Extra\nBob,25";
        var result = await _validator.ValidateAsync(csv, context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("columns", result.ErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncEmptyCsv()
    {
        var context = new OutputValidationContext(OutputFormat.Csv);
        var result = await _validator.ValidateAsync("", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ShouldTextFormatAlwaysValid_WhenValidateAsync()
    {
        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await _validator.ValidateAsync("anything goes here", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnFormatValidator_WhenName()
    {
        Assert.Equal("FormatValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn10_WhenPriority()
    {
        Assert.Equal(10, _validator.Priority);
    }
}
