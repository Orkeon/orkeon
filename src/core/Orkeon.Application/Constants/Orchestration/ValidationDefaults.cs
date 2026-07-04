namespace Orkeon.Application.Constants.Orchestration;

/// <summary>
/// Validation-related default values for output retry logic.
/// </summary>
public static class ValidationDefaults
{
    /// <summary>Default maximum number of output validation retries before accepting the last output.</summary>
    public const int DefaultMaxOutputRetries = 2;
}
