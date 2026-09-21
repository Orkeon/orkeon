using Orkeon.Compliance.Vfs;
using Orkeon.Domain.Common;
using Orkeon.Studio.Core.FileSystem;

namespace Orkeon.Studio.Core.Teams;

/// <summary>Where a team mount comes from, once the sidecar is read against this machine's settings (VFS-90).</summary>
public enum TeamMountSource
{
    /// <summary>A folder inside the team (<c>./output:/output:rw</c>); it carries no id and is laid as <c>--mount</c>.</summary>
    InsideTeam,

    /// <summary>A settings entry of this machine — named by id, or matched by folder, root and rights; laid as <c>--mount-id</c> when the entry has an id.</summary>
    Settings,

    /// <summary>A copy the settings do not hold as recorded; the team's own intent, laid as <c>--mount</c>.</summary>
    Copy,

    /// <summary>An id no settings entry of this machine carries: the launch is refused until it is declared (D-06).</summary>
    UnknownId,

    /// <summary>An entry the parser refuses; passed to the runner, which reports it.</summary>
    Unreadable,
}

/// <summary>One team mount as the launchers and the screens see it.</summary>
/// <param name="Raw">The sidecar's spelling.</param>
/// <param name="Effective">
/// The mount string in force for this machine: the settings entry itself for a
/// <see cref="TeamMountSource.Settings"/> mount (the id wins over the copy, D-02), the
/// resolved absolute folder for an in-team one, the raw spelling otherwise.
/// </param>
/// <param name="Source">Where it comes from.</param>
/// <param name="Id">The id it carries or resolved to, if any.</param>
/// <param name="SettingsEntry">The settings entry it stands for, for a <see cref="TeamMountSource.Settings"/> mount.</param>
public sealed record ResolvedTeamMount(
    string Raw,
    string Effective,
    TeamMountSource Source,
    MountId? Id,
    MountDefinition? SettingsEntry);

/// <summary>
/// Reads a sidecar's mounts against the settings, once, at the catalog's boundary (VFS-90):
/// an entry that names a settings declaration by id stands for that declaration as it is
/// today; an older copy is matched by what it declares; the team's own folders resolve under
/// the team; anything else is the team's own intent. Every launcher and every screen then
/// reasons on the same answer.
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a team's mounts name user-owned folders " +
    "on the physical disk, and this resolves them before any VFS mount exists — it reads nothing.")]
public static class TeamMountResolution
{
    /// <summary>
    /// Resolves <paramref name="sidecarMounts"/> for a team living in <paramref name="teamDirectory"/>.
    /// </summary>
    /// <param name="teamDirectory">The team folder, which team-relative entries resolve under.</param>
    /// <param name="sidecarMounts">The sidecar's entries; null or empty yields an empty list.</param>
    /// <param name="declaredMounts">
    /// The settings' <c>Orkeon:FileSystem:Mounts</c>, or null when the settings are not
    /// consulted — an id-carrying entry is then a <see cref="TeamMountSource.Copy"/>, never an
    /// <see cref="TeamMountSource.UnknownId"/>: a screen that did not look must not alarm.
    /// </param>
    public static IReadOnlyList<ResolvedTeamMount> Resolve(
        string teamDirectory,
        IReadOnlyList<string>? sidecarMounts,
        IReadOnlyList<string>? declaredMounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        if (sidecarMounts is not { Count: > 0 } mounts)
            return [];

        var declared = declaredMounts is null ? null : Parse(declaredMounts);
        return mounts.Select(raw => ResolveOne(teamDirectory, raw, declared)).ToList();
    }

    /// <summary>One sidecar entry, read against the team folder and the declarations.</summary>
    private static ResolvedTeamMount ResolveOne(string teamDirectory, string raw, List<MountDefinition>? declared)
    {
        if (!MountDefinition.TryParse(raw, out var mount, out _))
            return new ResolvedTeamMount(raw, raw, TeamMountSource.Unreadable, null, null);

        if (TeamMountPaths.IsTeamRelative(raw))
            return new ResolvedTeamMount(raw, TeamMountPaths.Resolve(teamDirectory, raw), TeamMountSource.InsideTeam, null, null);

        if (mount.Id is { } id)
            return ResolveById(raw, id, declared);

        if (declared?.FirstOrDefault(entry => entry.SameDeclaration(mount)) is { } same)
            return new ResolvedTeamMount(raw, same.ToMountString(), TeamMountSource.Settings, same.Id, same);

        return DeclaredMounts.IsInsideTeam(raw, teamDirectory)
            ? new ResolvedTeamMount(raw, raw, TeamMountSource.InsideTeam, null, null)
            : new ResolvedTeamMount(raw, raw, TeamMountSource.Copy, null, null);
    }

    /// <summary>
    /// An entry carrying an id: the declaration it names when the settings were consulted
    /// and hold it, a copy when they were not consulted, unknown when they were and do not.
    /// </summary>
    private static ResolvedTeamMount ResolveById(string raw, MountId id, List<MountDefinition>? declared)
    {
        if (declared is null)
            return new ResolvedTeamMount(raw, raw, TeamMountSource.Copy, id, null);

        return declared.FirstOrDefault(entry => id.Equals(entry.Id)) is { } byId
            ? new ResolvedTeamMount(raw, byId.ToMountString(), TeamMountSource.Settings, id, byId)
            : new ResolvedTeamMount(raw, raw, TeamMountSource.UnknownId, id, null);
    }

    private static List<MountDefinition> Parse(IReadOnlyList<string> mountStrings)
    {
        var parsed = new List<MountDefinition>(mountStrings.Count);
        foreach (var entry in mountStrings)
        {
            if (MountDefinition.TryParse(entry, out var mount, out _))
                parsed.Add(mount);
        }

        return parsed;
    }
}
