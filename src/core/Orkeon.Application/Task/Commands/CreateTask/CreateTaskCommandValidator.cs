using Orkeon.Application.Validation;

namespace Orkeon.Application.Task.Commands.CreateTask;

/// <summary>
/// Validates <see cref="CreateTaskCommand"/> before it reaches the handler.
/// </summary>
public sealed class CreateTaskCommandValidator : ICommandValidator<CreateTaskCommand>
{
    /// <inheritdoc />
    public ValidationResult Validate(CreateTaskCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Description))
        {
            errors.Add("Description is required.");
        }
        else if (command.Description.Length > 5000)
        {
            errors.Add("Description must not exceed 5000 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.ExpectedOutput))
        {
            errors.Add("ExpectedOutput is required.");
        }
        else if (command.ExpectedOutput.Length > 2000)
        {
            errors.Add("ExpectedOutput must not exceed 2000 characters.");
        }

        if (!string.IsNullOrWhiteSpace(command.AgentId) && !Guid.TryParse(command.AgentId, out _))
        {
            errors.Add($"AgentId '{command.AgentId}' is not a valid GUID format.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
