namespace Orkeon.Application.Validation;

/// <summary>
/// Result of a validation (an <see cref="ICommandValidator{TCommand}"/>'s verdict on a command).
/// </summary>
public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Gets a formatted error message combining all errors.
    /// </summary>
    public string GetErrorMessage() => string.Join("; ", Errors);
}
