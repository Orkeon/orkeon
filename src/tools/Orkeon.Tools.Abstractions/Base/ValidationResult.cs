namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Result of input validation.
/// </summary>
public record ValidationResult(
    bool IsValid,
    string? Error = null)
{
    /// <summary>Returns a successful validation result.</summary>
    public static ValidationResult Success() => new(true);

    /// <summary>Returns a failed validation result with the specified error message.</summary>
    /// <param name="error">The validation error message.</param>
    public static ValidationResult Failed(string error) => new(false, error);
}
