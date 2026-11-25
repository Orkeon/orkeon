using Microsoft.Extensions.Configuration;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;

namespace Orkeon.Host.Files;

/// <summary>
/// The refusals of a start with <c>Orkeon:Host:Files</c> on: a root an agent could read by another mount would show every
/// conversation to every crew, and a mount on <c>/inbox</c> or <c>/outbox</c> would stand where a conversation's does.
/// </summary>
[Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves the conversations' root and the mounts' folders on the physical disk at start, before any run.")]
internal static class HostFilesStartup
{
    /// <summary>The roots no mount may claim.</summary>
    public static readonly string[] Roots = [RunnerVirtualRoots.Inbox, RunnerVirtualRoots.Outbox];

    /// <summary>The root of the conversations that <paramref name="virtualPath"/> claims, or null.</summary>
    public static string? Claimed(string virtualPath) =>
        Array.Find(Roots, root => RunnerExecution.ClaimsRoot(virtualPath, root));

    /// <summary>
    /// Why the host cannot keep its conversations' files where the settings say, or null once the folder is there.
    /// </summary>
    /// <param name="options">The host's settings.</param>
    /// <param name="boot">Every mount of the host, the crews' folders included.</param>
    public static string? Refusal(OrkeonHostOptions options, FileSystemRegistry boot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(boot);

        var key = $"{HostFilesOptions.SectionName}:Root";
        var root = Path.GetFullPath(options.Files.Root);
        if (PhysicalRealPath.Resolve(root) is null)
            return $"{key} '{options.Files.Root}' is a loop of symbolic links.";

        foreach (var mount in boot.GetMounts())
        {
            if (Overlap(root, mount.BasePath))
                return $"{key} '{options.Files.Root}' is, holds or is under the folder mounted on '{mount.VirtualPath}': give the conversations a folder no mount reaches.";
        }

        foreach (var crew in options.Crews)
        {
            foreach (var mount in CrewRunner.GrantedMounts(crew))
            {
                if (Overlap(root, mount.BasePath))
                    return $"{key} '{options.Files.Root}' is, holds or is under the folder granted to crew '{crew.Name}' on '{mount.VirtualPath}': give the conversations a folder no mount reaches.";
            }
        }

        try
        {
            new ConversationStore(root).CreateRoot();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{key} '{options.Files.Root}' cannot be made: {ex.Message}";
        }
    }

    /// <summary>
    /// What the operator must know when an agent's shell is not confined: it reads the folders of every conversation.
    /// Null when the settings ask for a confinement.
    /// </summary>
    public static string? ShellWarning(IConfiguration configuration, bool inContainer)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var mode = configuration["Orkeon:Tools:Shell:Confinement:Mode"];
        var unconfined = string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase)
            || (inContainer && (string.IsNullOrEmpty(mode) || string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase)));
        return unconfined
            ? $"WARNING: {HostFilesOptions.SectionName} is on and the shell is not confined: a crew with shell_command reads the files of every conversation."
            : null;
    }

    /// <summary>Whether this process runs in a container, where the shell is not confined unless the settings say so.</summary>
    public static bool InContainer() =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/.dockerenv")
        || File.Exists("/run/.containerenv");

    // As written and as the links lead: either way to the same bytes is one way too many.
    private static bool Overlap(string root, string mounted)
    {
        string[] roots = [root, PhysicalRealPath.Resolve(root) ?? root];
        var folder = Path.GetFullPath(mounted);
        string[] folders = [folder, PhysicalRealPath.Resolve(folder) ?? folder];
        return roots.Any(r => folders.Any(f => PhysicalPathContainment.IsUnder(r, f) || PhysicalPathContainment.IsUnder(f, r)));
    }
}
