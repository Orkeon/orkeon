using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class PiiDetectionValidatorTests
{
    private readonly PiiDetectionValidator _validator = new();

    private static OutputValidationContext DefaultContext =>
        new(ExpectedFormat: OutputFormat.Text);

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncNoPii()
    {
        var result = await _validator.ValidateAsync(
            "The stock price of AAPL increased by 3.2% today.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncEmailDetected()
    {
        var result = await _validator.ValidateAsync(
            "Please contact john.doe@example.com for more info.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Email", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncSsnDetected()
    {
        var result = await _validator.ValidateAsync(
            "SSN: 123-45-6789", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("SSN", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncPhoneNumberDetected()
    {
        var result = await _validator.ValidateAsync(
            "Call me at +1-555-123-4567 for details.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Phone", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncCreditCardDetected()
    {
        var result = await _validator.ValidateAsync(
            "Card number: 4111-1111-1111-1111", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Credit card", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncIpAddressDetected()
    {
        var result = await _validator.ValidateAsync(
            "Server IP: 192.168.1.100", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("IPv4", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReportAll_WhenValidateAsyncMultiplePiiTypes()
    {
        var result = await _validator.ValidateAsync(
            "Email: test@test.com, IP: 10.0.0.1", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Email", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IPv4", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldHaveSuggestedFix_WhenValidateAsync()
    {
        var result = await _validator.ValidateAsync(
            "Contact john@example.com", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.SuggestedFix);
        Assert.Contains("redact", result.SuggestedFix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldReturnPiiDetectionValidator_WhenName()
    {
        Assert.Equal("PiiDetectionValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn50_WhenPriority()
    {
        Assert.Equal(50, _validator.Priority);
    }
}
