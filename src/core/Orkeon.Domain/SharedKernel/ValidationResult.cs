namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    /// <summary>
    /// Gets whether the validation was successful (no errors).
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors.AsReadOnly();

    /// <summary>
    /// Creates a new ValidationResult with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public ValidationResult(params ValidationError[] errors)
    {
        _errors.AddRange(errors);
    }

    /// <summary>
    /// Creates a new ValidationResult with the specified errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public ValidationResult(IEnumerable<ValidationError> errors)
    {
        _errors.AddRange(errors);
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A validation result with no errors.</returns>
    public static ValidationResult Success() => new();

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    /// <param name="propertyName">The name of the property that failed validation.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A validation result with the specified error.</returns>
    public static ValidationResult Failure(string propertyName, string errorMessage)
    {
        return new ValidationResult(new ValidationError(propertyName, errorMessage));
    }

    /// <summary>
    /// Creates a failed validation result with multiple errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <returns>A validation result with the specified errors.</returns>
    public static ValidationResult Failure(params ValidationError[] errors)
    {
        return new ValidationResult(errors);
    }

    /// <summary>
    /// Adds an error to this validation result.
    /// </summary>
    /// <param name="propertyName">The name of the property that failed validation.</param>
    /// <param name="errorMessage">The error message.</param>
    public void AddError(string propertyName, string errorMessage)
    {
        _errors.Add(new ValidationError(propertyName, errorMessage));
    }

    /// <summary>
    /// Adds an error to this validation result.
    /// </summary>
    /// <param name="error">The validation error to add.</param>
    public void AddError(ValidationError error)
    {
        _errors.Add(error);
    }

    /// <summary>
    /// Combines this validation result with another validation result.
    /// </summary>
    /// <param name="other">The other validation result to combine with.</param>
    /// <returns>A new validation result containing errors from both results.</returns>
    public ValidationResult Combine(ValidationResult other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var combinedErrors = new List<ValidationError>(_errors);
        combinedErrors.AddRange(other._errors);
        return new ValidationResult(combinedErrors);
    }

    /// <summary>
    /// Gets a string representation of all validation errors.
    /// </summary>
    /// <returns>A formatted string containing all error messages.</returns>
    public override string ToString()
    {
        if (IsValid)
            return "Validation successful";

        return $"Validation failed with {_errors.Count} error(s):\n" +
               string.Join("\n", _errors.Select(e => $"- {e.PropertyName}: {e.ErrorMessage}"));
    }
}
