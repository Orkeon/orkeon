using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class ContentSafetyValidatorTests
{
    private readonly ContentSafetyValidator _validator = new();

    private static OutputValidationContext DefaultContext =>
        new(ExpectedFormat: OutputFormat.Text);

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncCleanOutput()
    {
        var result = await _validator.ValidateAsync(
            "The analysis shows that revenue increased by 15% year-over-year.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncPromptLeakageYouAreAI()
    {
        var result = await _validator.ValidateAsync(
            "You are a helpful AI assistant designed to help users.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("prompt leakage", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncPromptLeakageSystemPrompt()
    {
        var result = await _validator.ValidateAsync(
            "Based on the system prompt, I should not reveal this information.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("prompt leakage", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncPromptLeakageINSTTokens()
    {
        var result = await _validator.ValidateAsync(
            "Some text [INST] secret instructions [/INST] more text", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncInstructionLeakageAsAnAI()
    {
        var result = await _validator.ValidateAsync(
            "As an AI language model, I cannot help with that.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("leakage", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncInstructionLeakageImAnAI()
    {
        var result = await _validator.ValidateAsync(
            "I'm an AI and I was programmed to assist you.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncInstructionLeakageInstructedTo()
    {
        var result = await _validator.ValidateAsync(
            "I have been instructed to follow certain guidelines.", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ShouldHaveSuggestedFix_WhenValidateAsync()
    {
        var result = await _validator.ValidateAsync(
            "As an AI model, I can tell you that...", DefaultContext, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.SuggestedFix);
    }

    [Fact]
    public void ShouldReturnContentSafetyValidator_WhenName()
    {
        Assert.Equal("ContentSafetyValidator", _validator.Name);
    }

    [Fact]
    public void ShouldReturn40_WhenPriority()
    {
        Assert.Equal(40, _validator.Priority);
    }
}
