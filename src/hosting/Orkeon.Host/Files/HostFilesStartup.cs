using Microsoft.Extensions.Configuration;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tools.Code;

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

        if (Reaching(root, boot.GetMounts()) is { } mount)
        {
            return $"{key} '{options.Files.Root}' is, holds or is under the folder mounted on '{mount.VirtualPath}': give the conversations a folder no mount reaches.";
        }

        foreach (var crew in options.Crews)
        {
            if (Reaching(root, CrewRunner.GrantedMounts(crew)) is { } granted)
            {
                return $"{key} '{options.Files.Root}' is, holds or is under the folder granted to crew '{crew.Name}' on '{granted.VirtualPath}': give the conversations a folder no mount reaches.";
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
    /// What the operator must know when an agent's shell, or the code it runs, is not confined: it reads the folders of every conversation.
    /// Null when the settings ask for a confinement.
    /// </summary>
    public static string? ShellWarning(IConfiguration configuration, bool inContainer)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // By name or by number, as the settings are read; what is neither refuses the start elsewhere.
        var written = configuration["Orkeon:Tools:Shell:Confinement:Mode"];
        var mode = string.IsNullOrWhiteSpace(written) ? ShellConfinementMode.Auto
            : Enum.TryParse<ShellConfinementMode>(written, ignoreCase: true, out var read) ? read
            : (ShellConfinementMode?)null;
        var tools = new List<string>();
        if (mode == ShellConfinementMode.None || (inContainer && mode == ShellConfinementMode.Auto))
            tools.Add("the shell is not confined (shell_command)");
        if (bool.TryParse(configuration["Orkeon:CodeSandbox:AllowHostExecution"], out var onHost) && onHost)
            tools.Add("Orkeon:CodeSandbox:AllowHostExecution lets code run on the host (code_interpreter)");
        return tools.Count > 0
            ? $"WARNING: {HostFilesOptions.SectionName} is on and {string.Join(", and ", tools)}: a crew with such a tool reads the files of every conversation."
            : null;
    }

    /// <summary>Lets the path validator reach the conversations' root, when it does not already.</summary>
    public static void Allow(PathSecurityOptions options, string root)
    {
        ArgumentNullException.ThrowIfNull(options);
        var full = new ConversationStore(root).Root;
        var workspace = Path.GetFullPath(options.DefaultWorkspaceRoot ?? Directory.GetCurrentDirectory());
        var real = PhysicalRealPath.Resolve(full) ?? full;
        if (!PhysicalPathContainment.IsUnder(full, workspace)
            || !PhysicalPathContainment.IsUnder(real, PhysicalRealPath.Resolve(workspace) ?? workspace))
        {
            options.AdditionalAllowedDirectories.Add(full);
        }
    }

    /// <summary>Whether this process runs in a container, where the shell is not confined unless the settings say so.</summary>
    public static bool InContainer() =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/.dockerenv")
        || File.Exists("/run/.containerenv");

    /// <summary>The first of the mounts that reaches the conversations' root now, or null: a link may lead there since the start.</summary>
    public static FileSystemMount? Reaching(string root, IEnumerable<FileSystemMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        return mounts.FirstOrDefault(mount => PhysicalOverlap.BetweenFolders(root, mount.BasePath));
    }
}
