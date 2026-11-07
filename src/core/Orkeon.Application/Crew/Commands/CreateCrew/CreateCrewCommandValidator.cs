using Orkeon.Application.Common;
using Orkeon.Application.Validation;

namespace Orkeon.Application.Crew.Commands.CreateCrew;

/// <summary>
/// Validates <see cref="CreateCrewCommand"/> before it reaches the handler.
/// </summary>
public sealed class CreateCrewCommandValidator : ICommandValidator<CreateCrewCommand>
{
    /// <inheritdoc />
    public ValidationResult Validate(CreateCrewCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors.Add("Name is required.");
        }
        else if (command.Name.Length > 200)
        {
            errors.Add("Name must not exceed 200 characters.");
        }

        if (command.Goal is { Length: > 2000 })
        {
            errors.Add("Goal must not exceed 2000 characters.");
        }

        if (command.AgentIds != null)
        {
            foreach (var agentId in command.AgentIds)
            {
                if (!AgentIdText.TryParse(agentId, out _))
                {
                    errors.Add($"Agent ID '{agentId}' is not a valid agent id (a ULID or a GUID).");
                }
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
