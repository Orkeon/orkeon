namespace Orkeon.Infrastructure.Constants.Security;

/// <summary>
/// Default values for sandbox execution configuration.
/// Centralises timeout constants used in process isolation and Docker sandboxes.
/// </summary>
public static class SandboxDefaults
{
    /// <summary>Timeout in seconds for container availability check.</summary>
    public const int ContainerTimeoutSeconds = 10;

    /// <summary>Timeout in seconds for building a sandbox project.</summary>
    public const int BuildTimeoutSeconds = 60;

    /// <summary>Extra time in seconds added for Docker overhead (grace period before kill).</summary>
    public const int KillGracePeriodSeconds = 90;
}
