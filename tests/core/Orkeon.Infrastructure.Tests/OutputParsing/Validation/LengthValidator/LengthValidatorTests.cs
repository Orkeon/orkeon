using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class LengthValidatorTests
{
    private readonly LengthValidator _validator = new();

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncWithinBounds()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            MinLength: 5,
            MaxLength: 100);

        var result = await _validator.ValidateAsync("This is a valid output.", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncTooShort()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            MinLength: 50);

        var result = await _validator.ValidateAsync("Short", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("too short", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50", result.ErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncTooLong()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            MaxLength: 10);

        var result = await _validator.ValidateAsync("This output is way too long for the limit", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("too long", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", result.ErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncNoConstraints()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text);

        var result = await _validator.ValidateAsync("Anything", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncExactMinLength()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            MinLength: 5);

        var result = await _validator.ValidateAsync("Hello", context, TestContext.Current.CancellationToken); // exactly 5 chars

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncExactMaxLength()
    {
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Text,
            MaxLength: 5);

        var result = await _validator.ValidateAsync("Hello", context, TestContext.Current.CancellationToken); // exactly 5 chars

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldReturnLengthValidator_WhenName()
    {
        Assert.Equal("LengthValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn5_WhenPriority()
    {
        Assert.Equal(5, _validator.Priority);
    }
}
