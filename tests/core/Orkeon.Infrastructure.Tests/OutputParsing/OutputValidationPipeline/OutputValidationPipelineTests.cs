using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing;

public class OutputValidationPipelineTests
{
    private static readonly string[] s_orderedValidators = ["First", "Second", "Third"];

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncAllValidatorsPass()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new AlwaysPassValidator("V1", 10));
        pipeline.AddValidator(new AlwaysPassValidator("V2", 20));

        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await pipeline.ValidateAsync("some output", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Results.Count);
        Assert.Null(result.CombinedErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnNotValid_WhenValidateAsyncOneValidatorFails()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new AlwaysPassValidator("V1", 10));
        pipeline.AddValidator(new AlwaysFailValidator("V2", 20, "Something is wrong"));

        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await pipeline.ValidateAsync("some output", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Results.Count);
        Assert.Contains("Something is wrong", result.CombinedErrorMessage);
    }

    [Fact]
    public async Task ShouldReturnCombinedErrors_WhenValidateAsyncAllValidatorsFail()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new AlwaysFailValidator("V1", 10, "Error A"));
        pipeline.AddValidator(new AlwaysFailValidator("V2", 20, "Error B"));

        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await pipeline.ValidateAsync("some output", context, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("Error A", result.CombinedErrorMessage);
        Assert.Contains("Error B", result.CombinedErrorMessage);
    }

    [Fact]
    public async Task ShouldValidatorsRunInPriorityOrder_WhenValidateAsync()
    {
        var executionOrder = new List<string>();
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new TrackingValidator("Third", 30, executionOrder));
        pipeline.AddValidator(new TrackingValidator("First", 10, executionOrder));
        pipeline.AddValidator(new TrackingValidator("Second", 20, executionOrder));

        var context = new OutputValidationContext(OutputFormat.Text);
        await pipeline.ValidateAsync("some output", context, TestContext.Current.CancellationToken);

        Assert.Equal(s_orderedValidators, executionOrder);
    }

    [Fact]
    public async Task ShouldReturnIsValid_WhenValidateAsyncEmptyPipeline()
    {
        var pipeline = new OutputValidationPipeline();
        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await pipeline.ValidateAsync("some output", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task ShouldWithRealValidatorsIntegratesCorrectly_WhenValidateAsync()
    {
        var pipeline = new OutputValidationPipeline(
        [
            new LengthValidator(),
            new FormatValidator()
        ]);

        var context = new OutputValidationContext(
            ExpectedFormat: OutputFormat.Json,
            MinLength: 5);

        var result = await pipeline.ValidateAsync("""{"key":"value"}""", context, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ShouldFailedResultsIncludeValidatorName_WhenValidateAsync()
    {
        var pipeline = new OutputValidationPipeline();
        pipeline.AddValidator(new AlwaysFailValidator("MyValidator", 10, "test error"));

        var context = new OutputValidationContext(OutputFormat.Text);
        var result = await pipeline.ValidateAsync("output", context, TestContext.Current.CancellationToken);

        Assert.Single(result.Results);
        Assert.Equal("MyValidator", result.Results[0].ValidatorName);
    }

    [Fact]
    public void ShouldReturnSameInstanceForChaining_WhenAddValidator()
    {
        var pipeline = new OutputValidationPipeline();
        var returned = pipeline.AddValidator(new AlwaysPassValidator("V1", 10));

        Assert.Same(pipeline, returned);
    }

    // --- Test Doubles ---

    private class AlwaysPassValidator : IOutputValidator
    {
        public string Name { get; }
        public int Priority { get; }

        public AlwaysPassValidator(string name, int priority)
        {
            Name = name;
            Priority = priority;
        }

        public Task<OutputValidationResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct)
            => Task.FromResult(new OutputValidationResult(IsValid: true));
    }

    private class AlwaysFailValidator : IOutputValidator
    {
        private readonly string _errorMessage;
        public string Name { get; }
        public int Priority { get; }

        public AlwaysFailValidator(string name, int priority, string errorMessage)
        {
            Name = name;
            Priority = priority;
            _errorMessage = errorMessage;
        }

        public Task<OutputValidationResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct)
            => Task.FromResult(new OutputValidationResult(IsValid: false, ErrorMessage: _errorMessage));
    }

    private class TrackingValidator : IOutputValidator
    {
        private readonly List<string> _executionOrder;
        public string Name { get; }
        public int Priority { get; }

        public TrackingValidator(string name, int priority, List<string> executionOrder)
        {
            Name = name;
            Priority = priority;
            _executionOrder = executionOrder;
        }

        public Task<OutputValidationResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct)
        {
            _executionOrder.Add(Name);
            return Task.FromResult(new OutputValidationResult(IsValid: true));
        }
    }
}
