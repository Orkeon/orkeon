using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// What of a team's folders a launch has to say on the command line at all (STUDIO-15 D-05).
/// <para>
/// A team never declares a folder, it associates one the settings already declare — and the
/// chooser records the settings entry verbatim in the sidecar. Laid on the run as
/// <c>--mount</c>, that entry meets its own twin from the settings: the runner now treats the
/// pair as one mount (the <c>--mount</c> replaces the settings entry by root, D-01), but
/// passing a machine default back to the machine is noise on the command line and, on an
/// engine from before D-01, the "Duplicate virtual paths" refusal that made every adopted
/// team unlaunchable from Studio. So an entry the settings already hold — same folder, same
/// name, same rights — is not laid. Anything else is: the same folder under another name is
/// a mount of its own, and the same name over another folder or with other rights is a
/// deliberate replacement the effective-mounts table shows as such.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: compares the mount strings a team recorded with the ones the settings " +
    "declare, before any VFS mount exists. Nothing is opened — this is string comparison.")]
public static class LaunchMountPlan
{
    /// <summary>
    /// The team mounts worth laying as <c>--mount</c>: every entry of
    /// <paramref name="teamMounts"/> except the ones <paramref name="settingsMounts"/> already
    /// holds — same physical folder (normalized, compared the way
    /// <see cref="PhysicalPathContainment"/> compares paths on this platform), same virtual
    /// root (ordinal, trailing slash ignored), same rights and sub-path overrides. Order is
    /// preserved. An entry the parser refuses passes through: the runner reports it with its
    /// own message, and dropping it here would hide that message.
    /// </summary>
    public static IReadOnlyList<string> WithoutSettingsDuplicates(
        IReadOnlyList<string> teamMounts,
        IReadOnlyList<string> settingsMounts)
    {
        ArgumentNullException.ThrowIfNull(teamMounts);
        ArgumentNullException.ThrowIfNull(settingsMounts);

        var declared = new List<MountDefinition>(settingsMounts.Count);
        foreach (var entry in settingsMounts)
        {
            if (MountDefinition.TryParse(entry, out var mount, out _) && mount is not null)
                declared.Add(mount);
        }

        var laid = new List<string>(teamMounts.Count);
        foreach (var entry in teamMounts)
        {
            if (MountDefinition.TryParse(entry, out var mount, out _) && mount is not null
                && declared.Any(settings => IsSameMount(mount, settings)))
            {
                continue;
            }

            laid.Add(entry);
        }

        return laid;
    }

    private static bool IsSameMount(MountDefinition team, MountDefinition settings) =>
        string.Equals(NormalizeFolder(team.PhysicalPath), NormalizeFolder(settings.PhysicalPath), PhysicalPathContainment.Comparison)
        && string.Equals(NormalizeRoot(team.VirtualPath), NormalizeRoot(settings.VirtualPath), StringComparison.Ordinal)
        && team.Rights == settings.Rights
        && team.Overrides.SequenceEqual(settings.Overrides);

    /// <summary>
    /// The folder as the settings and the sidecar spell it, less the trailing separator a
    /// picker or a hand edit may have left — the same normalization
    /// <c>DeclaredMounts.IsDeclared</c> vouches for a folder with.
    /// </summary>
    private static string NormalizeFolder(string path) => path.Trim().TrimEnd('/', '\\');

    private static string NormalizeRoot(string virtualPath) =>
        virtualPath.Length > 1 ? virtualPath.TrimEnd('/') : virtualPath;
}
