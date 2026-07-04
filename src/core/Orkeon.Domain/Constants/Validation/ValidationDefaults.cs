namespace Orkeon.Domain.Constants.Validation;

/// <summary>
/// Default values for validation operations.
/// Centralises magic numbers used across domain validators and security checks.
/// </summary>
public static class ValidationDefaults
{
    /// <summary>
    /// Timeout in seconds for regex matching operations to prevent ReDoS attacks.
    /// </summary>
    public const int RegexTimeoutSeconds = 5;
}
