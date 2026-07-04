namespace Orkeon.Domain.Constants.Platform;

/// <summary>
/// Platform-wide version and currency constants for the Orkeon platform.
/// Centralises versioning and cost-related magic values.
/// </summary>
public static class PlatformDefaults
{
    /// <summary>Current Orkeon platform version string used in API responses and tool metadata.</summary>
    public const string Version = "1.0.0";

    /// <summary>Default ISO 4217 currency code used for cost calculations.</summary>
    public const string DefaultCurrency = "USD";
}
