using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class CompletenessValidatorTests
{
    private readonly CompletenessValidator _validator = new();

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncAllRequiredFieldsPresent()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            RequiredFields: ["name", "age"]);

        var result = await _validator.ValidateAsync(
            """{"name":"Alice","age":30}""", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncMissingRequiredField()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            RequiredFields: ["name", "age", "email"]);

        var result = await _validator.ValidateAsync(
            """{"name":"Alice","age":30}""", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("email", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncNoRequiredFields()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json);

        var result = await _validator.ValidateAsync("anything", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncEmptyRequiredFields()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            RequiredFields: []);

        var result = await _validator.ValidateAsync("anything", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldCheckFieldInText_WhenValidateAsyncTextFormat()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            RequiredFields: ["Summary", "Status"]);

        var output = "Summary: All good\nStatus: Complete";
        var result = await _validator.ValidateAsync(output, context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncTextFormatMissingField()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            RequiredFields: ["Summary", "Conclusion"]);

        var output = "Summary: All good\nStatus: Complete";
        var result = await _validator.ValidateAsync(output, context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Conclusion", result.ErrorMessage);
    }

    [Fact]
    public async Task ShouldJsonCaseInsensitiveFieldPresent_WhenValidateAsync()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            RequiredFields: ["Name"]);

        var result = await _validator.ValidateAsync(
            """{"name":"Alice"}""", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnCompletenessValidator_WhenName()
    {
        Assert.Equal("CompletenessValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn30_WhenPriority()
    {
        Assert.Equal(30, _validator.Priority);
    }
}
