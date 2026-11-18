namespace Orkeon.Domain.Constants.Platform;

/// <summary>
/// Platform-wide version and currency constants for the Orkeon platform.
/// Centralises versioning and cost-related magic values.
/// </summary>
public static class PlatformDefaults
{
    /// <summary>
    /// A fixed value kept for the callers that compiled it in: nothing in Orkeon reads it, and it is not the version
    /// of the running release (#418).
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>Default ISO 4217 currency code used for cost calculations.</summary>
    public const string DefaultCurrency = "USD";
}
