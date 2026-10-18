using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>Whether the host process runs inside a container.</summary>
internal interface IContainerDetector
{
    /// <summary>True when the process runs inside a container.</summary>
    bool IsInContainer();
}

/// <summary>
/// Tells a container from a host: <c>/.dockerenv</c> (Docker), <c>/run/.containerenv</c> (Podman) or
/// <c>DOTNET_RUNNING_IN_CONTAINER=true</c> (the official .NET images). In a container the container is
/// the boundary, so <see cref="ShellConfinementMode.Auto"/> leaves the shell unconfined there.
/// </summary>
/// <param name="fileExists">Whether a host file exists; the host's file system by default.</param>
/// <param name="environment">Reads an environment variable; the process's by default.</param>
internal sealed class ContainerDetector(Func<string, bool> fileExists, Func<string, string?> environment) : IContainerDetector
{
    /// <summary>The environment variable the official .NET container images set to <c>true</c>.</summary>
    public const string RunningInContainerVariable = "DOTNET_RUNNING_IN_CONTAINER";

    /// <summary>The files a container runtime leaves at the root of the container.</summary>
    internal static readonly string[] MarkerFiles = ["/.dockerenv", "/run/.containerenv"];

    /// <summary>The detector over the host's file system and environment.</summary>
    public static ContainerDetector Host { get; } = new(HostFileExists, Environment.GetEnvironmentVariable);

    /// <inheritdoc />
    public bool IsInContainer()
        => string.Equals(environment(RunningInContainerVariable)?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
           || MarkerFiles.Any(fileExists);

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the marker files a container runtime leaves at its root, as the bubblewrap probe looks for bwrap; not a VFS path.")]
    private static bool HostFileExists(string path)
    {
        // OUT-OF-SCOPE: system probing of a container marker file, not a VFS mount.
        return File.Exists(path);
    }
}
