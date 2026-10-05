using System.Text.RegularExpressions;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Teams;

/// <summary>One tree of a team in a workshop folder: its kind (the first-level folder's name) and its path.</summary>
/// <param name="Kind">
/// <c>teams</c>, <c>workbooks</c>, <c>tests</c>, <c>settings</c>, or a whole <c>mounts.&lt;name&gt;</c>.
/// </param>
/// <param name="Path">The team's folder under that tree.</param>
public sealed record WorkshopTree(string Kind, string Path);

/// <summary>
/// The layout of an Orkeon Workshop folder, seen from the teams root Studio was given
/// (STUDIO-61): <c>teams/&lt;slug&gt;/</c> is Studio's catalogue, and beside it the workshop
/// keeps <c>workbooks/&lt;slug&gt;/</c>, <c>tests/&lt;slug&gt;/</c>,
/// <c>settings/&lt;slug&gt;/appsettings.json</c>, one <c>mounts.&lt;name&gt;/&lt;slug&gt;/</c> per
/// set of mount folders, and <c>archive/</c>. The slug of the team folder indexes everything.
/// <para>
/// A workshop is recognised by its layout, never by a marker file: <c>settings/</c> and
/// <c>workbooks/</c> both exist beside the teams root. An ordinary <c>~/Orkeon/teams</c> has
/// neither. Every answer is tolerant — an unreadable folder is no tree —, and no enumeration
/// follows a reparse point.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the workshop folder is user-owned " +
    "storage on the physical disk, addressed before any VFS mount exists.")]
public static partial class WorkshopLayout
{
    // EXCEPTION-BOOTSTRAP: the workshop folder is the physical directory that holds the teams
    // root, probed before any VFS mount exists.

    /// <summary>The first-level folder holding the teams: the teams root's own name in a workshop.</summary>
    public const string TeamsFolder = "teams";

    /// <summary>The first-level folder of the workbooks (decisions, approvals).</summary>
    public const string WorkbooksFolder = "workbooks";

    /// <summary>The first-level folder of the tests (evidence).</summary>
    public const string TestsFolder = "tests";

    /// <summary>The first-level folder of each team's Orkeon settings.</summary>
    public const string SettingsFolder = "settings";

    /// <summary>The first-level folder the workshop moves removed teams into.</summary>
    public const string ArchiveFolder = "archive";

    /// <summary>The settings file of a team, under <see cref="SettingsFolder"/>/&lt;slug&gt;/.</summary>
    public const string SettingsFileName = "appsettings.json";

    /// <summary>The fixed trees of a team, in the order <see cref="TreesOf"/> lists them; the mount sets follow.</summary>
    private static readonly string[] FixedTrees = [WorkbooksFolder, TestsFolder, SettingsFolder];

    /// <summary>
    /// The workshop folder: the parent of the teams root, as the user sees it — a junction used
    /// as the root is not followed. The root itself when it has no parent.
    /// </summary>
    public static string RootOf(string teamsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsRoot);

        var trimmed = Path.TrimEndingDirectorySeparator(teamsRoot);
        return Path.GetDirectoryName(trimmed) is { Length: > 0 } parent ? parent : trimmed;
    }

    /// <summary>
    /// Whether the teams root sits in a workshop folder: <see cref="SettingsFolder"/> and
    /// <see cref="WorkbooksFolder"/> both exist beside it. One of the two alone is not a workshop.
    /// </summary>
    public static bool IsWorkshop(string teamsRoot)
    {
        var root = RootOf(teamsRoot);
        return DirectoryExists(Path.Combine(root, SettingsFolder)) && DirectoryExists(Path.Combine(root, WorkbooksFolder));
    }

    /// <summary>
    /// The settings file the workshop keeps for a team — <c>settings/&lt;slug&gt;/appsettings.json</c>
    /// beside the teams root —, whether or not it exists.
    /// </summary>
    public static string SettingsFileOf(string teamsRoot, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return Path.Combine(RootOf(teamsRoot), SettingsFolder, slug, SettingsFileName);
    }

    /// <summary>
    /// The trees a team has in the workshop, existing ones only: its team folder, then
    /// <c>workbooks</c>, <c>tests</c>, <c>settings</c>, then each <c>mounts.&lt;name&gt;</c> set
    /// holding a folder for it, in ordinal order. A folder named like a mount set but not
    /// spelled <c>mounts.</c> plus lowercase letters, digits and dashes is not one.
    /// </summary>
    public static IReadOnlyList<WorkshopTree> TreesOf(string teamsRoot, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var root = RootOf(teamsRoot);
        var trees = new List<WorkshopTree>();

        var team = Path.Combine(Path.TrimEndingDirectorySeparator(teamsRoot), slug);
        if (DirectoryExists(team))
            trees.Add(new WorkshopTree(TeamsFolder, team));

        foreach (var kind in FixedTrees)
        {
            var path = Path.Combine(root, kind, slug);
            if (DirectoryExists(path))
                trees.Add(new WorkshopTree(kind, path));
        }

        foreach (var set in MountSetsOf(root))
        {
            var path = Path.Combine(root, set, slug);
            if (DirectoryExists(path))
                trees.Add(new WorkshopTree(set, path));
        }

        return trees;
    }

    /// <summary>The <c>mounts.&lt;name&gt;</c> folders of the workshop, reparse points left aside, in ordinal order.</summary>
    private static List<string> MountSetsOf(string root)
    {
        List<string> sets = [];
        try
        {
            var directory = new DirectoryInfo(root);
            if (!directory.Exists)
                return sets;

            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (MountSetName().IsMatch(child.Name))
                    sets.Add(child.Name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable workshop folder has no mount sets to list.
        }

        sets.Sort(StringComparer.Ordinal);
        return sets;
    }

    private static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex("^mounts\\.[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MountSetName();
}
