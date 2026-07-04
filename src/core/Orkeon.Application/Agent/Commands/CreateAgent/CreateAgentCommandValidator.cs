using Orkeon.Application.Validation;

namespace Orkeon.Application.Agent.Commands.CreateAgent;

/// <summary>
/// Validates <see cref="CreateAgentCommand"/> before it reaches the handler.
/// </summary>
public sealed class CreateAgentCommandValidator : ICommandValidator<CreateAgentCommand>
{
    /// <inheritdoc />
    public ValidationResult Validate(CreateAgentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Role))
        {
            errors.Add("Role is required.");
        }
        else if (command.Role.Length > 200)
        {
            errors.Add("Role must not exceed 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.Goal))
        {
            errors.Add("Goal is required.");
        }
        else if (command.Goal.Length > 2000)
        {
            errors.Add("Goal must not exceed 2000 characters.");
        }

        if (command.Backstory is { Length: > 5000 })
        {
            errors.Add("Backstory must not exceed 5000 characters.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
