using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.Base;

#region Test helpers

public record LengthCheckInput
{
    public string Text { get; init; } = "";
    public int MinLength { get; init; }
    public int MaxLength { get; init; } = int.MaxValue;
}

public record LengthCheckResult
{
    public double Score { get; init; }
    public int ActualLength { get; init; }
    public string Reasoning { get; init; } = "";
}

/// <summary>
/// Concrete test evaluator that checks text length compliance.
/// </summary>
public class LengthCheckEvaluator : global::Orkeon.Infrastructure.Evaluation.Base.EvaluatorBase<LengthCheckInput, LengthCheckResult>
{
    public override string Name => "length_check";
    public override string Description => "Checks that output meets length requirements";

    public string? ValidationOverride { get; set; }

    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["text"] = input.Output,
            ["min_length"] = 10
        };

        if (input.Metadata != null && input.Metadata.TryGetValue("max_length", out var maxLen))
        {
            parameters["max_length"] = int.Parse(maxLen);
        }

        return parameters;
    }

    protected override double ExtractScore(LengthCheckResult result) => result.Score;

    protected override string? ExtractReasoning(LengthCheckResult result) => result.Reasoning;

    protected override string? ValidateTypedRequest(LengthCheckInput request)
        => ValidationOverride;

    protected override Task<LengthCheckResult> ExecuteTypedAsync(LengthCheckInput request, CancellationToken ct)
    {
        var length = request.Text.Length;
        var meetsMin = length >= request.MinLength;
        var meetsMax = length <= request.MaxLength;
        var score = (meetsMin && meetsMax) ? 1.0 : 0.0;

        return Task.FromResult(new LengthCheckResult
        {
            Score = score,
            ActualLength = length,
            Reasoning = meetsMin && meetsMax
                ? $"Length {length} is within [{request.MinLength}, {request.MaxLength}]"
                : $"Length {length} is outside [{request.MinLength}, {request.MaxLength}]"
        });
    }
}

#endregion

public class EvaluatorBaseTests
{
    private readonly EvaluatorBaseTestsFixture _fixture = new();

    [Fact]
    public async Task ShouldReturnCorrectScore_WhenInputIsValid()
    {
        var input = new EvaluationInput(
            Output: "This is a sufficiently long text for testing purposes.");

        var score = await _fixture.EvaluateAsync(input);

        Assert.Equal("length_check", score.EvaluatorName);
        Assert.Equal(1.0, score.Score);
        Assert.NotNull(score.Reasoning);
        Assert.True(score.IsPassing());
    }

    [Fact]
    public async Task ShouldReturnZeroScore_WhenInputIsShort()
    {
        var input = new EvaluationInput(Output: "Short");

        var score = await _fixture.EvaluateAsync(input);

        Assert.Equal("length_check", score.EvaluatorName);
        Assert.Equal(0.0, score.Score);
        Assert.False(score.IsPassing());
    }

    [Fact]
    public async Task ShouldUseMaxLength_WhenMetadataContainsMaxLength()
    {
        var metadata = new Dictionary<string, string> { ["max_length"] = "5" };
        var input = new EvaluationInput(Output: "This is too long for the max", Metadata: metadata);

        var score = await _fixture.EvaluateAsync(input);

        Assert.Equal(0.0, score.Score);
        Assert.Contains("outside", score.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnZeroWithReason_WhenValidationFails()
    {
        var score = await _fixture
            .WithValidationOverride("Text cannot be processed")
            .EvaluateAsync(new EvaluationInput(Output: "Some text that is long enough"));

        Assert.Equal(0.0, score.Score);
        Assert.Contains("Validation failed", score.Reasoning);
        Assert.Contains("Text cannot be processed", score.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnDetails_WhenEvaluating()
    {
        var input = new EvaluationInput(
            Output: "A text that is long enough for the test to pass.");

        var score = await _fixture.EvaluateAsync(input);

        Assert.NotNull(score.Details);
        Assert.True(score.Details!.ContainsKey("score"));
        Assert.True(score.Details.ContainsKey("actual_length"));
    }

    [Fact]
    public async Task ShouldReturnReasoningString_WhenExtractingReasoning()
    {
        var input = new EvaluationInput(
            Output: "This is definitely long enough text for testing.");

        var score = await _fixture.EvaluateAsync(input);

        Assert.NotNull(score.Reasoning);
        Assert.Contains("within", score.Reasoning);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenAccessingProperties()
    {
        var evaluator = _fixture.GetEvaluator();
        Assert.Equal("length_check", evaluator.Name);
        Assert.Equal("Checks that output meets length requirements", evaluator.Description);
        Assert.False(evaluator.RequiresLlm);
    }

    [Fact]
    public async Task ShouldMapOutputToText_WhenBuildingParameters()
    {
        var input = new EvaluationInput(
            Output: "The actual output text that should be checked");

        var score = await _fixture.EvaluateAsync(input);

        Assert.Equal(1.0, score.Score);
    }
}
