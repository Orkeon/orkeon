using Orkeon.Application.Crew.Commands.CreateCrew;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Tests.Validation;

public class CreateCrewCommandValidatorTests
{
    private readonly CreateCrewCommandValidator _validator = new();

    [Fact]
    public void ShouldPass_WhenCommandHasValidName()
    {
        var command = new CreateCrewCommand("Dev Team", "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldFail_WhenNameIsNull()
    {
        var command = new CreateCrewCommand(null!, "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void ShouldFail_WhenNameIsEmpty()
    {
        var command = new CreateCrewCommand("", "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void ShouldFail_WhenNameIsWhitespace()
    {
        var command = new CreateCrewCommand("   ", "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void ShouldFail_WhenNameExceeds200Characters()
    {
        var longName = new string('a', 201);
        var command = new CreateCrewCommand(longName, "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("200"));
    }

    [Fact]
    public void ShouldPass_WhenNameIsExactly200Characters()
    {
        var exactName = new string('a', 200);
        var command = new CreateCrewCommand(exactName, "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldFail_WhenGoalExceeds2000Characters()
    {
        var longGoal = new string('g', 2001);
        var command = new CreateCrewCommand("Dev Team", longGoal, ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("2000"));
    }

    [Fact]
    public void ShouldPass_WhenGoalIsNull()
    {
        var command = new CreateCrewCommand("Dev Team", null!, ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenGoalIsExactly2000Characters()
    {
        var exactGoal = new string('g', 2000);
        var command = new CreateCrewCommand("Dev Team", exactGoal, ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenAgentIdsIsNull()
    {
        var command = new CreateCrewCommand("Dev Team", "Build software", ProcessType.Sequential, null);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenAgentIdsAreValidGuids()
    {
        var agentIds = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        var command = new CreateCrewCommand("Dev Team", "Build software", ProcessType.Sequential, agentIds);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldFail_WhenAgentIdsContainInvalidGuid()
    {
        var agentIds = new List<string> { Guid.NewGuid().ToString(), "not-a-guid" };
        var command = new CreateCrewCommand("Dev Team", "Build software", ProcessType.Sequential, agentIds);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not-a-guid"));
    }

    [Fact]
    public void ShouldCollectMultipleErrors_WhenMultipleAgentIdsAreInvalid()
    {
        var agentIds = new List<string> { "bad-1", "bad-2" };
        var command = new CreateCrewCommand("Dev Team", "Build software", ProcessType.Sequential, agentIds);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
