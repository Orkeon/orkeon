using Orkeon.Application.Agent.Commands.CreateAgent;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Validation;

namespace Orkeon.Application.Tests.Validation;

public class ValidatingCommandHandlerTests
{
    private class StubHandler : ICommandHandler<CreateAgentCommand, AgentDto>
    {
        public bool WasCalled { get; private set; }

        public System.Threading.Tasks.Task<AgentDto> HandleAsync(CreateAgentCommand command, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return System.Threading.Tasks.Task.FromResult(new AgentDto { Id = "1", Name = command.Role, Role = command.Role, Goal = command.Goal, Backstory = "", Type = "standard", Status = "Active" });
        }
    }

    private class AlwaysValidValidator : ICommandValidator<CreateAgentCommand>
    {
        public ValidationResult Validate(CreateAgentCommand command)
            => new(true, []);
    }

    private class AlwaysInvalidValidator : ICommandValidator<CreateAgentCommand>
    {
        public ValidationResult Validate(CreateAgentCommand command)
            => new(false, ["Something is wrong."]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDelegateToInner_WhenValidationPasses()
    {
        // Arrange
        var inner = new StubHandler();
        var handler = new ValidatingCommandHandler<CreateAgentCommand, AgentDto>(inner, new AlwaysValidValidator());
        var command = new CreateAgentCommand("Dev", "Code", null, null);

        // Act
        var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(inner.WasCalled);
        Assert.Equal("Dev", result.Role);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrow_WhenValidationFails()
    {
        // Arrange
        var inner = new StubHandler();
        var handler = new ValidatingCommandHandler<CreateAgentCommand, AgentDto>(inner, new AlwaysInvalidValidator());
        var command = new CreateAgentCommand("Dev", "Code", null, null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<CommandValidationException>(
            () => handler.HandleAsync(command, TestContext.Current.CancellationToken));

        Assert.False(inner.WasCalled);
        Assert.Single(ex.Errors);
        Assert.Contains("Something is wrong.", ex.Errors);
    }

    [Fact]
    public void ShouldContainAllErrors_WhenExceptionIsCreated()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        var ex = new CommandValidationException(errors);

        // Assert
        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains("Error 1", ex.Message);
        Assert.Contains("Error 2", ex.Message);
    }
}
