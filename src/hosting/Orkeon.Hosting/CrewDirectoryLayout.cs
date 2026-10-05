using Orkeon.Constants.FileSystem;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Hosting;

/// <summary>
/// Outcome of inspecting a candidate crew directory.
/// </summary>
/// <param name="IsCrewDirectory">
/// <see langword="true"/> when the path is a directory holding a crew definition that
/// <c>ICrewFactory.CreateFromDirectoryAsync</c> can load.
/// </param>
/// <param name="Error">
/// A ready-to-print diagnostic when the path is a directory that cannot be loaded — either
/// ambiguous (a YAML layout side by side with a scripting entry point) or carrying no
/// recognized layout at all. <see langword="null"/> when the path is not a directory
/// (the caller keeps its file-based dispatch) or when the directory is a valid crew.
/// </param>
/// <param name="CrewRoot">
/// The directory holding the definition the loader reads, as a full physical path: the
/// inspected directory itself, or its <c>crew/</c> sub-folder when that one is the crew
/// (STUDIO-59). <see langword="null"/> when <paramref name="IsCrewDirectory"/> is false.
/// </param>
public sealed record CrewDirectoryInspection(bool IsCrewDirectory, string? Error, string? CrewRoot = null);

/// <summary>
/// Detects whether a crew target path is a <b>crew directory</b> rather than a single file, so
/// runners can route <c>run &lt;dir&gt;</c> to <c>ICrewFactory.CreateFromDirectoryAsync</c>.
/// Two layouts are recognized, both handled by <c>YamlCrewDefinitionLoader.LoadFromDirectoryAsync</c>:
/// <list type="bullet">
///   <item><description><b>per-entity</b> — <c>config.yaml</c> (or <c>crew.yaml</c>) plus an
///   <c>agents/</c> and/or <c>tasks/</c> sub-directory of one file per entity;</description></item>
///   <item><description><b>flat</b> — the legacy triplet <c>crew.yaml</c> + <c>agents.yaml</c> +
///   <c>tasks.yaml</c>.</description></item>
/// </list>
/// A directory whose <c>crew/</c> sub-folder holds one of those layouts — the team folder
/// <c>forge promote</c> writes, one folder per mount point beside the definition — is a crew
/// whose definition is <c>crew/</c>, whatever the root holds (STUDIO-59): a mount point named
/// <c>agents/</c> or a launcher script at the root is set aside, never read as a crew. The
/// descent is one step, never into <c>crew/crew/</c>, and a <c>crew/</c> that is no crew leaves
/// the root under its own rules. Orkeon Studio applies the same order
/// (<c>RunTargetDetector</c>), so a folder is read one way.
/// A directory that also holds a scripting entry point is rejected as ambiguous instead of
/// silently preferring one form over the other, and a directory with no recognized layout is
/// rejected with the list of what was searched.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: classifies the user-supplied crew target on the physical disk, before the host (and thus IFileSystemService) is built.")]
public static class CrewDirectoryLayout
{
    /// <summary>
    /// The sub-folder a promoted team keeps its definition in (mirrors the forge renderer's
    /// <c>CrewDirectoryName</c> and Studio's <c>RunTargetDetector.PromotedCrewDirectoryName</c>).
    /// </summary>
    public const string PromotedCrewFolder = "crew";

    /// <summary>Sub-directory names that select the per-entity layout.</summary>
    private static readonly string[] EntityFolders = ["agents", "tasks"];

    /// <summary>File names that make up the flat legacy layout (all three required).</summary>
    private static readonly IReadOnlyList<string> FlatFiles = ConventionalNames.FlatCrewLayoutFiles;

    /// <summary>Suffixes of the scripting entry points that would make a directory ambiguous.</summary>
    private static readonly string[] ScriptSuffixes = [".ork.ts", ".ork.js"];

    /// <summary>Glob patterns matching <see cref="ScriptSuffixes"/>.</summary>
    private static readonly string[] ScriptPatterns = [.. ScriptSuffixes.Select(suffix => "*" + suffix)];

    /// <summary>
    /// Classifies <paramref name="path"/> as a crew directory, a rejected directory (with a
    /// diagnostic), or "not a directory" (no diagnostic — the caller keeps its file dispatch).
    /// </summary>
    /// <param name="path">The crew target supplied on the command line.</param>
    public static CrewDirectoryInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(root))
            return new CrewDirectoryInspection(false, null);

        // The promoted layout first: a crew/ that is itself a crew is the team's definition,
        // and the root's folders and scripts are mount points and launchers. One step only.
        var promoted = Path.Combine(root, PromotedCrewFolder);
        if (Directory.Exists(promoted) && InspectRoot(promoted) is { IsCrewDirectory: true } team)
            return team;

        return InspectRoot(root);
    }

    /// <summary>
    /// Classifies <paramref name="root"/> on its own content — the per-entity or flat layout at
    /// its root, a scripting entry point beside it — without looking into a <c>crew/</c>
    /// sub-folder: this is the one step <see cref="Inspect"/> takes on it.
    /// </summary>
    private static CrewDirectoryInspection InspectRoot(string root)
    {
        var entityFolders = EntityFolders
            .Select(name => Path.Combine(root, name))
            .Where(Directory.Exists)
            .ToList();
        var isFlat = FlatFiles.All(name => File.Exists(Path.Combine(root, name)));
        var scripts = FindScripts(root);

        if (entityFolders.Count == 0 && !isFlat)
            return new CrewDirectoryInspection(false, DescribeUnrecognized(root, scripts));

        if (scripts.Count > 0)
        {
            var yamlMarkers = entityFolders.Count > 0
                ? entityFolders.Select(d => d + Path.DirectorySeparatorChar)
                : FlatFiles.Select(name => Path.Combine(root, name));
            return new CrewDirectoryInspection(false, DescribeAmbiguous(root, yamlMarkers, scripts));
        }

        return new CrewDirectoryInspection(true, null, root);
    }

    /// <summary>
    /// Scripting entry points sitting directly in the directory, ordinally sorted. The glob is
    /// only a hint to the file system — Windows still matches short 8.3 names, so
    /// <c>*.ork.ts</c> can return a file whose real name is not one — hence the suffix re-check
    /// on the results. Both patterns can return the same file, so duplicates are dropped.
    /// </summary>
    private static List<string> FindScripts(string root)
    {
        var scripts = ScriptPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly))
            .Where(HasScriptSuffix)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        scripts.Sort(StringComparer.Ordinal);
        return scripts;
    }

    private static bool HasScriptSuffix(string path) =>
        ScriptSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static string DescribeAmbiguous(string root, IEnumerable<string> yamlMarkers, IEnumerable<string> scripts)
        => $"Ambiguous crew directory '{root}': it holds a multi-file YAML crew "
            + $"({string.Join(", ", yamlMarkers.Select(Quote))}) AND a scripting entry point "
            + $"({string.Join(", ", scripts.Select(Quote))}). "
            + "Pass the intended target explicitly — the .ork.ts file, or a directory that holds only the YAML layout.";

    private static string DescribeUnrecognized(string root, List<string> scripts)
    {
        var searched = string.Join(", ",
            EntityFolders.Select(name => Quote(name + "/")).Append(Quote(string.Join(" + ", FlatFiles))));
        var message = $"'{root}' is a directory but holds no recognized crew layout. "
            + $"Searched for: {searched}, at its root or under a {Quote(PromotedCrewFolder + "/")} sub-folder.";

        // A lone script in the directory is the most likely intent behind `run <dir>` here, so
        // point at it rather than leaving the user to guess — without ever running it implicitly.
        return scripts.Count > 0
            ? message + $" Found a scripting entry point ({string.Join(", ", scripts.Select(Quote))}) — pass that file directly."
            : message;
    }

    private static string Quote(string value) => $"'{value}'";
}
