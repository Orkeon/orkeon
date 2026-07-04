using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Infrastructure.Flows.Base;

namespace Orkeon.Infrastructure.Tests.Flows.Base;

#region Test helpers

public record StepInput
{
    public string Message { get; init; } = "";
    public int Multiplier { get; init; } = 1;
}

public record StepOutput
{
    public string ProcessedMessage { get; init; } = "";
    public int ComputedValue { get; init; }
}

/// <summary>
/// Concrete test flow step for unit testing FlowStepBase.
/// </summary>
public class TestFlowStep : FlowStepBase<StepInput, StepOutput>
{
    public override string Name => "test_step";
    public override string Description => "A test flow step";

    public string? ValidationOverride { get; set; }
    public StepInput? LastInput { get; private set; }

    protected override string? ValidateTypedRequest(StepInput request)
        => ValidationOverride;

    protected override Task<StepOutput> ExecuteTypedAsync(StepInput request, CancellationToken ct)
    {
        LastInput = request;
        return Task.FromResult(new StepOutput
        {
            ProcessedMessage = $"Processed: {request.Message}",
            ComputedValue = request.Multiplier * 10
        });
    }
}

/// <summary>
/// Flow step that throws an exception for error testing.
/// </summary>
public class FailingFlowStep : FlowStepBase<StepInput, StepOutput>
{
    public override string Name => "failing_step";
    public override string Description => "A step that fails";

    protected override Task<StepOutput> ExecuteTypedAsync(StepInput request, CancellationToken ct)
    {
        throw new InvalidOperationException("Step execution failed intentionally");
    }
}

#endregion

public class FlowStepBaseTests
{
    private readonly TestFlowStep _step = new();

    [Fact]
    public async Task ShouldReturnSuccess_WhenExecuteAsyncValidFlowState()
    {
        var state = FlowState.Empty
            .Set("message", "hello")
            .Set("multiplier", 3);

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Output);
        Assert.NotNull(_step.LastInput);
        Assert.Equal("hello", _step.LastInput!.Message);
        Assert.Equal(3, _step.LastInput.Multiplier);
    }

    [Fact]
    public async Task ShouldOutputIsTypedResponse_WhenExecuteAsync()
    {
        var state = FlowState.Empty
            .Set("message", "world")
            .Set("multiplier", 2);

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var output = result.Output as StepOutput;
        Assert.NotNull(output);
        Assert.Equal("Processed: world", output!.ProcessedMessage);
        Assert.Equal(20, output.ComputedValue);
    }

    [Fact]
    public async Task ShouldContainSerializedOutput_WhenExecuteAsyncUpdatedContext()
    {
        var state = FlowState.Empty
            .Set("message", "test")
            .Set("multiplier", 1);

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.UpdatedContext);
        // The updated context should be created from the serialized output
        var dict = result.UpdatedContext.ToDictionary();
        Assert.True(dict.ContainsKey("processed_message") || dict.ContainsKey("processedMessage"));
    }

    [Fact]
    public async Task ShouldReturnFailure_WhenExecuteAsyncValidationFails()
    {
        _step.ValidationOverride = "Message cannot be empty";

        var state = FlowState.Empty
            .Set("message", "")
            .Set("multiplier", 1);

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Validation failed", result.Error);
        Assert.Contains("Message cannot be empty", result.Error);
    }

    [Fact]
    public async Task ShouldReturnFailure_WhenExecuteAsyncExceptionThrown()
    {
        var failingStep = new FailingFlowStep();
        var state = FlowState.Empty
            .Set("message", "trigger error")
            .Set("multiplier", 1);

        var result = await failingStep.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Step execution error", result.Error);
        Assert.Contains("intentionally", result.Error);
    }

    [Fact]
    public async Task ShouldUseDefaults_WhenExecuteAsyncMinimalState()
    {
        var state = FlowState.Empty
            .Set("message", "minimal");

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_step.LastInput);
        Assert.Equal("minimal", _step.LastInput!.Message);
        Assert.Equal(1, _step.LastInput.Multiplier); // default from record
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenProperties()
    {
        Assert.Equal("test_step", _step.Name);
        Assert.Equal("A test flow step", _step.Description);
        Assert.Empty(_step.Dependencies);
    }

    [Fact]
    public void ShouldToDictionaryCorrectDeserialization_WhenFlowState()
    {
        var state = FlowState.Empty
            .Set("message", "hello")
            .Set("multiplier", 5);

        var dict = state.ToDictionary();

        Assert.Equal("hello", dict["message"]);
        Assert.Equal(5, dict["multiplier"]);
    }

    [Fact]
    public async Task ShouldWorkCorrectly_WhenExecuteAsyncFlowStateFromDictionary()
    {
        var dict = new Dictionary<string, object>
        {
            ["message"] = "from dict",
            ["multiplier"] = 7
        };
        var state = FlowState.FromDictionary(dict);

        var result = await _step.ExecuteAsync(state, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(_step.LastInput);
        Assert.Equal("from dict", _step.LastInput!.Message);
        Assert.Equal(7, _step.LastInput.Multiplier);
    }

    [Fact]
    public async Task ShouldProduceCorrectResult_WhenCreateFailure()
    {
        var failure = FlowStepResult.CreateFailure("Something went wrong");

        Assert.False(failure.Success);
        Assert.Equal("Something went wrong", failure.Error);
        Assert.Null(failure.Output);
    }
}
