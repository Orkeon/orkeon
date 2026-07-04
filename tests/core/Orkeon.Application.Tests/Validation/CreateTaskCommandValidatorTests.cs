using Orkeon.Application.Task.Commands.CreateTask;

namespace Orkeon.Application.Tests.Validation;

public class CreateTaskCommandValidatorTests
{
    private readonly CreateTaskCommandValidator _validator = new();

    [Fact]
    public void ShouldPass_WhenCommandHasDescriptionAndExpectedOutput()
    {
        var command = new CreateTaskCommand("Analyze data", "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldFail_WhenDescriptionIsNull()
    {
        var command = new CreateTaskCommand(null!, "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Description"));
    }

    [Fact]
    public void ShouldFail_WhenDescriptionIsEmpty()
    {
        var command = new CreateTaskCommand("", "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Description"));
    }

    [Fact]
    public void ShouldFail_WhenDescriptionIsWhitespace()
    {
        var command = new CreateTaskCommand("   ", "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Description"));
    }

    [Fact]
    public void ShouldFail_WhenDescriptionExceeds5000Characters()
    {
        var longDesc = new string('d', 5001);
        var command = new CreateTaskCommand(longDesc, "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("5000"));
    }

    [Fact]
    public void ShouldPass_WhenDescriptionIsExactly5000Characters()
    {
        var exactDesc = new string('d', 5000);
        var command = new CreateTaskCommand(exactDesc, "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldFail_WhenExpectedOutputIsNull()
    {
        var command = new CreateTaskCommand("Analyze data", null!, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ExpectedOutput"));
    }

    [Fact]
    public void ShouldFail_WhenExpectedOutputIsEmpty()
    {
        var command = new CreateTaskCommand("Analyze data", "", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ExpectedOutput"));
    }

    [Fact]
    public void ShouldFail_WhenExpectedOutputIsWhitespace()
    {
        var command = new CreateTaskCommand("Analyze data", "   ", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ExpectedOutput"));
    }

    [Fact]
    public void ShouldFail_WhenExpectedOutputExceeds2000Characters()
    {
        var longOutput = new string('o', 2001);
        var command = new CreateTaskCommand("Analyze data", longOutput, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("2000"));
    }

    [Fact]
    public void ShouldPass_WhenExpectedOutputIsExactly2000Characters()
    {
        var exactOutput = new string('o', 2000);
        var command = new CreateTaskCommand("Analyze data", exactOutput, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenAgentIdIsNull()
    {
        var command = new CreateTaskCommand("Analyze data", "A detailed report", null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenAgentIdIsValidGuid()
    {
        var agentId = Guid.NewGuid().ToString();
        var command = new CreateTaskCommand("Analyze data", "A detailed report", agentId);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldFail_WhenAgentIdIsInvalidGuid()
    {
        var command = new CreateTaskCommand("Analyze data", "A detailed report", "not-a-guid");

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not-a-guid"));
    }

    [Fact]
    public void ShouldPass_WhenAgentIdIsEmptyString()
    {
        var command = new CreateTaskCommand("Analyze data", "A detailed report", "");

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldCollectMultipleErrors_WhenBothDescriptionAndOutputAreMissing()
    {
        var command = new CreateTaskCommand("", "", null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Description"));
        Assert.Contains(result.Errors, e => e.Contains("ExpectedOutput"));
    }
}
