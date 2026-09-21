using System.Collections.ObjectModel;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// Where a team folder row comes from: a folder inside the team, a settings declaration the
/// team names by id (VFS-90), or an id this machine has no declaration for (D-06).
/// </summary>
/// <param name="ShortId">The last six characters of the settings entry the team names; empty for an in-team folder.</param>
/// <param name="IsDeclared">Whether the row is a settings declaration rather than a folder inside the team.</param>
/// <param name="IsUnknownId">Whether the team names a declaration this machine does not have.</param>
public readonly record struct TeamFolderOrigin(string ShortId, bool IsDeclared, bool IsUnknownId)
{
    /// <summary>A folder inside the team.</summary>
    public static TeamFolderOrigin InsideTeam { get; } = new("", false, false);

    /// <summary>A settings declaration the team names, found on this machine.</summary>
    public static TeamFolderOrigin Declared(string shortId) => new(shortId, true, false);

    /// <summary>A declaration the team names that this machine does not have.</summary>
    public static TeamFolderOrigin Unknown(string shortId) => new(shortId, true, true);
}

/// <summary>
/// One folder of one adopted team, as the read-only « Team folders » section of
/// Settings › Authorized folders lists it (STUDIO-14, D-13): the team, the name the agents
/// use, the sub-folder inside the team behind it, and the rights — in the one-word badge of
/// the mount list (STUDIO-16), the full label as its tooltip.
/// </summary>
public sealed class TeamFolderRowViewModel
{
    internal TeamFolderRowViewModel(
        string teamName, string virtualPath, string folder, MountRights rights, IStudioStrings strings,
        TeamFolderOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(strings);

        TeamName = teamName;
        VirtualPath = virtualPath;
        Folder = folder;
        ShortId = origin.ShortId;
        IsDeclared = origin.IsDeclared;
        IsUnknownId = origin.IsUnknownId;
        IsReadWrite = rights != MountRights.ReadOnly;
        RightsLabel = MountRightsTokens.GetLabel(rights, strings);
        RightsBadge = MountRightsTokens.GetBadge(rights, strings);
        Label = origin switch
        {
            { IsUnknownId: true } => string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.TeamFoldersUnknownId], teamName, virtualPath, origin.ShortId),
            { IsDeclared: true } => string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.TeamFoldersRowDeclared], teamName, virtualPath, folder, origin.ShortId),
            _ => string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.TeamFoldersRow], teamName, virtualPath, folder),
        };
    }

    /// <summary>The last six characters of the settings entry the team names (VFS-90); empty for an in-team folder.</summary>
    public string ShortId { get; }

    /// <summary>Whether the row is a settings declaration the team names, rather than a folder inside the team.</summary>
    public bool IsDeclared { get; }

    /// <summary>Whether the team names a declaration this machine does not have (D-06).</summary>
    public bool IsUnknownId { get; }

    /// <summary>The team's display name — one line, like its card (STUDIO-16).</summary>
    public string TeamName { get; }

    /// <summary>The name the agents address (<c>/output</c>).</summary>
    public string VirtualPath { get; }

    /// <summary>The sub-folder inside the team folder (<c>output</c>, <c>input</c>) — never a disk path (ADR-008).</summary>
    public string Folder { get; }

    /// <summary>The full rights label — the badge's tooltip.</summary>
    public string RightsLabel { get; }

    /// <summary>The one-word rights badge (STUDIO-16).</summary>
    public string RightsBadge { get; }

    /// <summary>True for a folder the team writes to.</summary>
    public bool IsReadWrite { get; }

    /// <summary>The whole line: « Veille concurrentielle · /output → output ».</summary>
    public string Label { get; }
}

/// <summary>
/// The « Team folders » section of Settings › Authorized folders (STUDIO-14, D-13 — the
/// display half of P-1): every folder an adopted team keeps inside its own directory, read
/// from the sidecars, listed for information and for nothing else.
/// <para>
/// A team's own folders are vouched for by living inside the team
/// (<see cref="DeclaredMounts.IsInsideTeam"/>) and are never written to the global
/// <c>Orkeon:FileSystem:Mounts</c>: that would duplicate the sidecar and put one team's
/// private folders in the list every other team picks from. The settings screen still has to
/// show them, or the folders the agents can see would be missing from the one screen that
/// claims to list them. So this section reads and never writes — no command, no ✕: a novice
/// looking for one is told, by the hint, that these folders are changed from « My teams ».
/// </para>
/// <para>
/// It reads the raw sidecar entries (<see cref="TeamSummary.Metadata"/>) rather than the
/// resolved <see cref="TeamSummary.Mounts"/>: the resolved list is absolute by design, and
/// the sub-folder's name is exactly what a team-relative entry spells. An older sidecar that
/// recorded the same folder absolute under the team is listed too — it is the same folder.
/// A folder outside the team is the settings' own business and does not appear here.
/// </para>
/// </summary>
public sealed class TeamFoldersViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TeamSummary>> _loadTeams;
    private readonly Func<IReadOnlyList<string>>? _declaredMounts;
    private readonly IStudioStrings _strings;

    /// <summary>Builds the section over the team catalog; the shell passes <c>TeamCatalog.List</c>.</summary>
    /// <param name="loadTeams">Reads the adopted teams.</param>
    /// <param name="strings">Localization port.</param>
    /// <param name="declaredMounts">
    /// Reads the settings' mounts, so a team folder that names a settings declaration by id
    /// (VFS-90) is listed too, with the declaration it resolves to; null lists the in-team
    /// folders alone.
    /// </param>
    public TeamFoldersViewModel(
        Func<IReadOnlyList<TeamSummary>> loadTeams,
        IStudioStrings? strings = null,
        Func<IReadOnlyList<string>>? declaredMounts = null)
    {
        ArgumentNullException.ThrowIfNull(loadTeams);

        _loadTeams = loadTeams;
        _declaredMounts = declaredMounts;
        _strings = strings ?? EnglishStudioStrings.Instance;
        // The rows carry fabricated text — the badge, the line — so a hot language switch
        // rebuilds them, the way every other list of this app re-emits (STUDIO-11).
        _strings.CultureChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>One row per in-team folder, teams in catalog order, entries in sidecar order.</summary>
    public ObservableCollection<TeamFolderRowViewModel> Rows { get; } = [];

    /// <summary>Whether any adopted team keeps a folder of its own.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>The empty state: no adopted team has a folder inside itself.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// Re-reads the catalog. The shell calls it whenever the set of teams on disk changed —
    /// an adoption, an import, a deletion, a duplication — and the folders tab calls it on
    /// arrival, so the section never shows a team that is gone or misses one just adopted.
    /// </summary>
    public void Refresh()
    {
        Rows.Clear();
        var declared = _declaredMounts?.Invoke();

        foreach (var team in _loadTeams())
        {
            foreach (var row in RowsOf(team, declared))
                Rows.Add(row);
        }

        OnPropertiesChanged(nameof(HasRows), nameof(IsEmpty));
    }

    /// <summary>The rows of one team, in sidecar order: the entries that resolve to a row.</summary>
    private IEnumerable<TeamFolderRowViewModel> RowsOf(TeamSummary team, IReadOnlyList<string>? declared)
    {
        if (team.Metadata?.Mounts is not { Count: > 0 } mounts)
            yield break;

        var teamName = TeamCatalog.NormalizeName(team.Name);
        foreach (var mount in mounts)
        {
            if (RowFor(teamName, team.Path, mount, declared) is { } row)
                yield return row;
        }
    }

    /// <summary>
    /// The row one sidecar entry makes: an in-team folder, a settings declaration the entry
    /// names by id (listed with what the declaration says today, or flagged when this machine
    /// has no such declaration — VFS-90), or nothing for an unreadable entry, a folder outside
    /// the team, or an id when the settings were not consulted.
    /// </summary>
    private TeamFolderRowViewModel? RowFor(string teamName, string teamPath, string mount, IReadOnlyList<string>? declared)
    {
        if (!MountDefinition.TryParse(mount, out var parsed, out _) || parsed is null)
            return null;

        // The one containment rule, not a local copy of it: relative entries are
        // inside the team by construction, an absolute one only when the path says so.
        if (DeclaredMounts.IsInsideTeam(mount, teamPath))
        {
            var folder = TeamMountPaths.TryGetRelativeFolder(mount, out var relative)
                ? relative
                : FolderUnderTeam(teamPath, parsed.PhysicalPath);

            return new TeamFolderRowViewModel(teamName, parsed.VirtualPath, folder, parsed.Rights, _strings, TeamFolderOrigin.InsideTeam);
        }

        if (declared is null || parsed.Id is null)
            return null;

        return DeclaredMounts.FindDeclared(mount, declared) is { } entry
            ? new TeamFolderRowViewModel(
                teamName, parsed.VirtualPath, entry.PhysicalPath, entry.Rights, _strings,
                TeamFolderOrigin.Declared(entry.ShortId ?? ""))
            : new TeamFolderRowViewModel(
                teamName, parsed.VirtualPath, "", parsed.Rights, _strings,
                TeamFolderOrigin.Unknown(parsed.ShortId ?? ""));
    }

    /// <summary>
    /// The sub-folder an older absolute sidecar entry names under the team, in the sidecar's
    /// own spelling (<c>/</c>): what the entry would read once the next save relativizes it.
    /// </summary>
    private static string FolderUnderTeam(string teamDirectory, string physicalPath)
    {
        try
        {
            return System.IO.Path.GetRelativePath(teamDirectory, physicalPath)
                .Replace(System.IO.Path.DirectorySeparatorChar, '/');
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.PathTooLongException or NotSupportedException)
        {
            // IsInsideTeam already resolved both paths, so a platform refusal here is not
            // reachable in practice; the folder's own name is still an honest row, and never
            // a disk path (ADR-008).
            return System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(physicalPath));
        }
    }
}
