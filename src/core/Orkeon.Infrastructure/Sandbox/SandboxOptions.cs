using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// Configuration options for the code sandbox.
/// Bound from configuration section "Orkeon:CodeSandbox".
/// </summary>
public class SandboxOptions
{
    /// <summary>
    /// Default execution timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum memory in bytes (default: 256 MB).
    /// </summary>
    public long MaxMemoryBytes { get; set; } = 256 * 1024 * 1024;

    /// <summary>
    /// Maximum output bytes (default: 50 KB).
    /// </summary>
    public long MaxOutputBytes { get; set; } = 50_000;

    /// <summary>
    /// Explicit, noisy opt-in to allow executing code on a sandbox that provides NO OS-level
    /// isolation (the host process runner) when an isolating sandbox (Docker) is unavailable.
    /// <para>
    /// Default is <c>false</c> (fail-closed): if no OS-isolating sandbox is available, code
    /// execution is REFUSED rather than silently falling back to the host. Set this to <c>true</c>
    /// only for trusted, non-LLM code in a trusted environment — it is RCE-equivalent on the host.
    /// </para>
    /// </summary>
    public bool AllowHostExecution { get; set; }

    /// <summary>
    /// Default permissions for sandbox executions.
    /// </summary>
    public SandboxPermissions DefaultPermissions { get; set; } = new();

    /// <summary>
    /// Default security analysis options.
    /// </summary>
    public SecurityAnalysisOptions SecurityOptions { get; set; } = new();
}

/// <summary>
/// Configuration options specific to the Docker sandbox.
/// </summary>
public class DockerSandboxOptions
{
    /// <summary>
    /// Docker image to use for code execution.
    /// </summary>
    public string ImageName { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0-alpine";

    /// <summary>
    /// Whether to pull the image on startup.
    /// </summary>
    public bool PullImageOnStartup { get; set; }
}
