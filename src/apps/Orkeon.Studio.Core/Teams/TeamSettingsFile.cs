using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// The settings file an Orkeon Workshop keeps for a team (STUDIO-62):
/// <c>settings/&lt;slug&gt;/appsettings.json</c> beside the teams root, which the workshop's
/// own launchers pass as <c>--settings</c>. A launch from Studio passes it the same way, under the
/// file the Expert mode pins and above the CLI's own resolution chain.
/// <para>
/// The file is found at that one place and nowhere else: an <c>appsettings.json</c> inside the
/// team, or under <c>crew/</c>, is not a source. The team is the folder right under the teams
/// root — the root Studio resolved, not the grand-parent the launchers guess — and its slug is
/// the folder's name, never the card's. Studio never writes this file.
/// </para>
/// </summary>
public static class TeamSettingsFile
{
    /// <summary>
    /// The workshop's settings file for the team behind <paramref name="targetPath"/>, when the
    /// team folder sits right under <paramref name="teamsRoot"/> and the file exists; null otherwise
    /// — no root, no target, a folder elsewhere, a file missing. The target may be the team folder
    /// itself, a file inside it, or the definition under a promoted team's <c>crew/</c> folder.
    /// </summary>
    /// <param name="teamsRoot">The teams root in force; null or blank finds nothing.</param>
    /// <param name="targetPath">What the launch is pointed at; null or blank finds nothing.</param>
    /// <param name="directories">The probe that says whether the target is a folder.</param>
    /// <param name="fileExists">The probe that says whether the settings file exists.</param>
    public static string? Find(
        string? teamsRoot,
        string? targetPath,
        IDirectoryProbe directories,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (string.IsNullOrWhiteSpace(teamsRoot))
            return null;

        if (TeamFolderOf(teamsRoot, targetPath, directories) is not { } team)
            return null;

        var slug = Path.GetFileName(Path.TrimEndingDirectorySeparator(team));
        if (slug.Length == 0)
            return null;

        var file = WorkshopLayout.SettingsFileOf(teamsRoot, slug);
        return fileExists(file) ? file : null;
    }

    /// <summary>
    /// The team folder right under the root that holds the target: the folder the launcher reads
    /// the sidecar from (<see cref="DeclaredMounts.TeamDirectoryOf"/>), or — when that folder is a
    /// promoted team's <c>crew/</c> — its parent. Null when neither sits right under the root.
    /// </summary>
    private static string? TeamFolderOf(string teamsRoot, string? targetPath, IDirectoryProbe directories)
    {
        if (DeclaredMounts.TeamDirectoryOf(targetPath, directories) is not { Length: > 0 } directory)
            return null;

        if (TeamCatalog.IsTeamFolderOf(teamsRoot, directory))
            return directory;

        // The same canonical spelling the catalogue keys its folders by: absolute, no trailing
        // separator, the input itself when the platform cannot parse it.
        var full = TeamCatalog.NormalizePath(directory);
        if (!string.Equals(Path.GetFileName(full), RunTargetDetector.PromotedCrewDirectoryName, StringComparison.Ordinal))
            return null;

        var parent = Path.GetDirectoryName(full);
        return parent is { Length: > 0 } && TeamCatalog.IsTeamFolderOf(teamsRoot, parent) ? parent : null;
    }
}
