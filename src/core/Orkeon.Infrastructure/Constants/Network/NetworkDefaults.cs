namespace Orkeon.Infrastructure.Constants.Network;

/// <summary>
/// Default network configuration values (ports, authority URLs, etc.).
/// </summary>
public static class NetworkDefaults
{
    /// <summary>Default port for the A2A (Agent-to-Agent) HTTP server.</summary>
    public const int A2APort = 5002;

    /// <summary>
    /// Azure AD authority URL template.
    /// Replace <c>{tenantId}</c> with the actual tenant identifier at runtime.
    /// </summary>
#pragma warning disable S1075 // URIs should not be hardcoded — this is the well-known Azure AD authority template
    public const string AzureAuthorityUrlTemplate = "https://login.microsoftonline.com/{tenantId}/v2.0";
#pragma warning restore S1075
}
