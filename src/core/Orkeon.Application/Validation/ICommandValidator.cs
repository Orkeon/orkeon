namespace Orkeon.Application.Validation;

/// <summary>
/// Generic interface for validating commands before they reach the handler.
/// </summary>
/// <typeparam name="TCommand">The type of command to validate.</typeparam>
public interface ICommandValidator<in TCommand>
{
    /// <summary>
    /// Validates the command and returns a result indicating success or failure.
    /// </summary>
    /// <param name="command">The command to validate.</param>
    /// <returns>A validation result with any errors found.</returns>
    ValidationResult Validate(TCommand command);
}
