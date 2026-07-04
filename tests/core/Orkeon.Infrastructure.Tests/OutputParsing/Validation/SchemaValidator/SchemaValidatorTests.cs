using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;
using JsonSchema = Orkeon.Domain.Task.JsonSchema;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class SchemaValidatorTests
{
    private readonly SchemaValidator _validator = new();

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnIsValid_WhenValidateAsyncOutputMatchesSchema()
    {
        var schema = JsonSchema.From("""
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                },
                "required": ["name", "age"]
            }
            """);

        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            Schema: schema);

        var result = await _validator.ValidateAsync(
            """{"name":"Alice","age":30}""", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNotValid_WhenValidateAsyncMissingRequiredField()
    {
        var schema = JsonSchema.From("""
            {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer"}
                },
                "required": ["name", "age"]
            }
            """);

        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            Schema: schema);

        var result = await _validator.ValidateAsync(
            """{"name":"Alice"}""", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("schema", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnIsValid_WhenValidateAsyncNoSchema()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            Schema: null);

        var result = await _validator.ValidateAsync("anything", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSkipValidation_WhenValidateAsyncNonJsonFormat()
    {
        var schema = JsonSchema.From("""{"type":"object"}""");
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Yaml,
            Schema: schema);

        var result = await _validator.ValidateAsync("key: value", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnNotValid_WhenValidateAsyncInvalidJsonWithSchema()
    {
        var schema = JsonSchema.From("""{"type":"object"}""");
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            Schema: schema);

        var result = await _validator.ValidateAsync("not json", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ShouldReturnSchemaValidator_WhenName()
    {
        Assert.Equal("SchemaValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn20_WhenPriority()
    {
        Assert.Equal(20, _validator.Priority);
    }
}
