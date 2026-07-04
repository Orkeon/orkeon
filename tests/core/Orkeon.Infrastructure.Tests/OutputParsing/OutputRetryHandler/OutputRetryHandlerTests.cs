using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing;

public class OutputRetryHandlerTests
{
    [Fact]
    public async Task ShouldReturnSuccess_WhenValidateOrRetryAsyncValidOutput()
    {
        var pipeline = new OutputValidationPipeline();
        // No validators = always valid
        var handler = new OutputRetryHandler(pipeline);
        var context = new OutputValidationContext(OutputFormat.Text);

        var result = await handler.ValidateOrRetryAsync("valid output", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal("valid output", result.Output);
        Assert.Null(result.CorrectionPrompt);
    }

    [Fact]
    public async Task ShouldReturnNeedsRetry_WhenValidateOrRetryAsyncInvalidOutput()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new FormatValidator());

        var handler = new OutputRetryHandler(pipeline);
        var context = new OutputValidationContext(OutputFormat.Json);

        var result = await handler.ValidateOrRetryAsync("not json", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.CorrectionPrompt);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ShouldInvalidOutputCorrectionPromptIncludesErrors_WhenValidateOrRetryAsync()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new FormatValidator());

        var handler = new OutputRetryHandler(pipeline);
        var context = new OutputValidationContext(OutputFormat.Json);

        var result = await handler.ValidateOrRetryAsync("not json", context, TestContext.Current.CancellationToken);

        Assert.Contains("Invalid JSON", result.CorrectionPrompt);
        Assert.Contains("Json", result.CorrectionPrompt);
    }

    [Fact]
    public void ShouldIncludeErrorsAndFormat_WhenBuildCorrectionPrompt()
    {
        var pipeline = new OutputValidationPipeline();
        var handler = new OutputRetryHandler(pipeline);

        var validationResult = new OutputPipelineResult(
            IsValid: false,
            Results:
            [
                new OutputValidationResult(
                    IsValid: false,
                    ErrorMessage: "Missing field: name",
                    SuggestedFix: "Add a name field",
                    ValidatorName: "CompletenessValidator")
            ],
            CombinedErrorMessage: "Missing field: name");

        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            RequiredFields: ["name", "age"]);

        var prompt = OutputRetryHandler.BuildCorrectionPrompt("bad output", validationResult, context);

        Assert.Contains("Missing field: name", prompt);
        Assert.Contains("Add a name field", prompt);
        Assert.Contains("Json", prompt);
        Assert.Contains("name", prompt);
        Assert.Contains("age", prompt);
    }

    [Fact]
    public void ShouldDefaultTo2_WhenMaxRetries()
    {
        var pipeline = new OutputValidationPipeline();
        var handler = new OutputRetryHandler(pipeline);

        Assert.Equal(2, handler.MaxRetries);
    }

    [Fact]
    public void ShouldBeAbleToBeConfigured_WhenMaxRetries()
    {
        var pipeline = new OutputValidationPipeline();
        var handler = new OutputRetryHandler(pipeline, maxRetries: 5);

        Assert.Equal(5, handler.MaxRetries);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenOutputRetryResultSuccess()
    {
        var result = OutputRetryResult.Success("the output");

        Assert.True(result.IsValid);
        Assert.Null(result.CorrectionPrompt);
        Assert.Equal("the output", result.Output);
    }

    [Fact]
    public void ShouldHaveCorrectProperties_WhenOutputRetryResultNeedsRetry()
    {
        var result = OutputRetryResult.NeedsRetry("fix this");

        Assert.False(result.IsValid);
        Assert.Equal("fix this", result.CorrectionPrompt);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task ShouldCombineErrors_WhenValidateOrRetryAsyncMultipleFailures()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new LengthValidator());
        pipeline.AddValidator(new FormatValidator());

        var handler = new OutputRetryHandler(pipeline);
        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            MinLength: 1000);

        var result = await handler.ValidateOrRetryAsync("not json", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        // Should include both length and format errors
        Assert.Contains("too short", result.CorrectionPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invalid JSON", result.CorrectionPrompt);
    }
}
