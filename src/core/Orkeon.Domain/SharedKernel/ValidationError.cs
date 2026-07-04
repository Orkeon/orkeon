namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Represents a validation error with property name, error message, optional error code,
/// and optional attempted value.
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Gets the name of the property that failed validation.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the error message describing why validation failed.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the error code for this validation error (optional).
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the attempted value that failed validation (optional).
    /// </summary>
    public object? AttemptedValue { get; }

    /// <summary>
    /// Initializes a new instance of the ValidationError class.
    /// </summary>
    /// <param name="propertyName">The name of the property that failed validation.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="errorCode">Optional error code.</param>
    /// <param name="attemptedValue">Optional attempted value that failed validation.</param>
    public ValidationError(string propertyName, string errorMessage, string? errorCode = null, object? attemptedValue = null)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        PropertyName = propertyName;
        ArgumentNullException.ThrowIfNull(errorMessage);
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        AttemptedValue = attemptedValue;
    }

    /// <summary>
    /// Returns a string representation of this validation error.
    /// </summary>
    /// <returns>A formatted string containing the property name and error message.</returns>
    public override string ToString()
    {
        var result = $"{PropertyName}: {ErrorMessage}";
        if (!string.IsNullOrEmpty(ErrorCode))
        {
            result += $" (Code: {ErrorCode})";
        }
        return result;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current validation error.
    /// </summary>
    /// <param name="obj">The object to compare with the current validation error.</param>
    /// <returns>True if the specified object is equal to the current validation error; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ValidationError other &&
               PropertyName == other.PropertyName &&
               ErrorMessage == other.ErrorMessage &&
               ErrorCode == other.ErrorCode;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current validation error.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, ErrorMessage, ErrorCode);
    }
}
