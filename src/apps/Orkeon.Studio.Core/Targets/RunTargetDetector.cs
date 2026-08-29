using Orkeon.Constants.FileSystem;
using System.Globalization;

namespace Orkeon.Studio.Core.Targets;

/// <summary>
/// Recognises what a picked path is, using the rules the CLI and the loaders already use:
/// <list type="bullet">
///   <item><description>a file, by extension — the same test as <c>RunCommand.IsYamlConfig</c>
///   for YAML, the scripting suffixes otherwise;</description></item>
///   <item><description>a directory holding a multi-file YAML crew — an <c>agents/</c> or
///   <c>tasks/</c> sub-folder, or the flat legacy triplet <c>crew.yaml</c> + <c>agents.yaml</c>
///   + <c>tasks.yaml</c>. Both forms are what <c>CrewDirectoryLayout.Inspect</c> accepts, and
///   they are what the CLI will run when handed the directory;</description></item>
///   <item><description>a directory holding <c>crew.ork.ts</c> — the scripting convention
///   (<c>ScriptHostFacadeOptions.CrewFileName</c>), or else its <c>*.ork.ts</c>/<c>*.ork.js</c>
///   files offered as candidates.</description></item>
/// </list>
/// A directory holding a YAML layout <em>and</em> any scripting entry point is refused as
/// ambiguous, exactly as <c>CrewDirectoryLayout.Inspect</c> refuses it: the CLI applies no
/// precedence there, and offers no flag to force one shape over the other.
/// </summary>
public sealed class RunTargetDetector
{
    /// <summary>Conventional script entry point in a crew directory (mirrors <c>ScriptHostFacadeOptions.CrewFileName</c>).</summary>
    public const string CrewScriptFileName = "crew.ork.ts";

    /// <summary>Per-entity agents sub-folder of the multi-file crew layout.</summary>
    public const string AgentsDirectoryName = "agents";

    /// <summary>Per-entity tasks sub-folder of the multi-file crew layout.</summary>
    public const string TasksDirectoryName = "tasks";

    /// <summary>
    /// Sub-folder holding the crew definition inside a promoted team folder — the layout
    /// <c>forge promote</c> writes (mirrors its <c>ForgeYamlRenderer.CrewDirectoryName</c>,
    /// and the one <c>TeamCatalog</c> already counts agents in).
    /// </summary>
    public const string PromotedCrewDirectoryName = Forge.ForgeRenderReader.CrewDirectoryName;

    /// <summary>Suffix of a scripting-DSL crew file.</summary>
    public const string ScriptSuffix = ".ork.ts";

    /// <summary>Suffix of an already-transpiled scripting-DSL crew file.</summary>
    public const string CompiledScriptSuffix = ".ork.js";

    /// <summary>
    /// The flat legacy YAML layout: all three files must be present for the directory to be
    /// one (mirrors <c>CrewDirectoryLayout.FlatFiles</c>).
    /// </summary>
    public static IReadOnlyList<string> FlatLayoutFileNames { get; } =
        ConventionalNames.FlatCrewLayoutFiles;

    /// <summary>Per-entity sub-folders that select the multi-file layout.</summary>
    private static readonly string[] EntityDirectoryNames = [AgentsDirectoryName, TasksDirectoryName];

    /// <summary>
    /// Scripting suffixes that make a YAML directory ambiguous — the same pair as
    /// <c>CrewDirectoryLayout.ScriptSuffixes</c>, not just the conventional entry point.
    /// </summary>
    private static readonly string[] ScriptSuffixes = [ScriptSuffix, CompiledScriptSuffix];

    /// <summary>Globs used to list the scripts of a directory.</summary>
    private static readonly string[] ScriptSearchPatterns = [.. ScriptSuffixes.Select(suffix => "*" + suffix)];

    private static readonly string[] YamlExtensions = [".yaml", ".yml"];

    private readonly ITargetProbe _probe;

    /// <summary>Creates a detector over the given probe (defaults to the real disk).</summary>
    public RunTargetDetector(ITargetProbe? probe = null) => _probe = probe ?? PhysicalTargetProbe.Instance;

    /// <summary>File extensions the CLI runs, as UI-ready text.</summary>
    public static string SupportedFileExtensions => ".yaml, .yml, .ork.ts, .js";

    /// <summary>
    /// Inspects <paramref name="path"/> and reports the shape it holds.
    /// </summary>
    /// <param name="path">A file or directory the user picked.</param>
    /// <param name="preferredDirectoryKind">
    /// How to read a directory that matches both directory shapes. Left null, such a
    /// directory is an error naming both candidates. Set to
    /// <see cref="RunTargetKind.ScriptDirectory"/>, it resolves the script the CLI will
    /// accept as a file. Set to <see cref="RunTargetKind.MultiFileCrewDirectory"/>, it is
    /// still an error — no CLI flag forces the YAML layout, so the launch would fail — but a
    /// dedicated one saying how to unblock it.
    /// </param>
    public RunTargetDetection Detect(string? path, RunTargetKind? preferredDirectoryKind = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RunTargetDetection.Failed(
                string.Empty,
                RunTargetCodes.EmptyPath,
                "No crew definition was selected: pick a .yaml/.ork.ts file or a crew directory.");
        }

        var selected = path.Trim();

        if (_probe.FileExists(selected))
            return DetectFile(selected);

        if (_probe.DirectoryExists(selected))
            return DetectDirectory(selected, preferredDirectoryKind);

        return RunTargetDetection.Failed(
            selected,
            RunTargetCodes.PathNotFound,
            string.Create(CultureInfo.InvariantCulture, $"'{selected}' is neither an existing file nor an existing directory."));
    }

    private static RunTargetDetection DetectFile(string filePath)
    {
        if (IsYamlFile(filePath))
        {
            return RunTargetDetection.Resolved(filePath, new RunTarget
            {
                Kind = RunTargetKind.YamlFile,
                SelectedPath = filePath,
                RunPath = filePath,
            });
        }

        if (IsScriptFile(filePath))
        {
            return RunTargetDetection.Resolved(filePath, new RunTarget
            {
                Kind = RunTargetKind.ScriptFile,
                SelectedPath = filePath,
                RunPath = filePath,
            });
        }

        return RunTargetDetection.Failed(
            filePath,
            RunTargetCodes.UnsupportedExtension,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{filePath}' is not a crew definition the CLI runs: expected one of {SupportedFileExtensions}."));
    }

    private RunTargetDetection DetectDirectory(
        string directory, RunTargetKind? preferredKind, bool allowPromotedLayout = true)
    {
        var markers = FindYamlLayoutMarkers(directory);
        var scripts = ListScripts(directory);
        var crewScript = scripts.Find(IsConventionalCrewScript);

        if (markers.Count > 0 && scripts.Count > 0)
            return ResolveContested(directory, markers, scripts, crewScript, preferredKind);

        if (markers.Count > 0)
            return ResolveMultiFile(directory, markers);

        if (crewScript is not null)
            return ResolveScriptDirectory(directory, crewScript);

        if (scripts.Count > 0)
            return RunTargetDetection.NeedsSelection(directory, scripts);

        // An adopted team: `forge promote` keeps the definition in a `crew/` sub-folder and
        // puts the launchers, the card, the deliverable folders and Studio's sidecar beside
        // it. The folder the user picks — and the one a team card hands over — is the team,
        // so the run path descends while the SELECTED path stays put: the sidecar is read
        // next to it, and the launch runs from the team folder, which is what makes the
        // team's own /output land inside the security root.
        if (allowPromotedLayout && _probe.DirectoryExists(Path.Combine(directory, PromotedCrewDirectoryName)))
        {
            var nested = DetectDirectory(
                Path.Combine(directory, PromotedCrewDirectoryName), preferredKind, allowPromotedLayout: false);

            if (nested is { Status: RunTargetDetectionStatus.Resolved, Target: { } inner })
                return RunTargetDetection.Resolved(directory, inner with { SelectedPath = directory });
        }

        return RunTargetDetection.Failed(
            directory,
            RunTargetCodes.NoCandidate,
            $"'{directory}' holds no crew definition: no '{AgentsDirectoryName}/' or '{TasksDirectoryName}/' "
            + $"sub-folder, no '{string.Join(" + ", FlatLayoutFileNames)}' triplet, no '{CrewScriptFileName}', "
            + $"no {DescribeScriptPatterns()} file. "
            + "Pick a .yaml or .ork.ts file inside it instead.");
    }

    /// <summary>
    /// Resolves a directory holding a YAML layout <em>and</em> at least one script. Only the
    /// script preference can resolve it: the CLI runs a script file happily, but rejects the
    /// directory itself whatever the user meant, so preferring the YAML layout can only be
    /// reported — never turned into a command line.
    /// </summary>
    private static RunTargetDetection ResolveContested(
        string directory,
        List<string> markers,
        List<string> scripts,
        string? crewScript,
        RunTargetKind? preferredKind)
    {
        if (preferredKind == RunTargetKind.ScriptDirectory)
        {
            return crewScript is not null
                ? ResolveScriptDirectory(directory, crewScript)
                : RunTargetDetection.NeedsSelection(directory, scripts);
        }

        if (preferredKind == RunTargetKind.MultiFileCrewDirectory)
        {
            return RunTargetDetection.Failed(
                directory,
                RunTargetCodes.YamlLayoutBlockedByScript,
                $"'{directory}' cannot be run as a multi-file YAML crew while it also holds a "
                + $"scripting entry point ({string.Join(", ", scripts)}): the CLI rejects such a "
                + "directory and has no flag that forces the YAML layout. Move or remove the "
                + "script(s) to run the YAML layout, or run the script instead.",
                [.. markers, .. scripts]);
        }

        return AmbiguousDirectory(directory, markers, scripts);
    }

    private static RunTargetDetection ResolveMultiFile(string directory, List<string> markers) =>
        RunTargetDetection.Resolved(directory, new RunTarget
        {
            Kind = RunTargetKind.MultiFileCrewDirectory,
            SelectedPath = directory,
            RunPath = directory,
            Markers = markers,
        });

    private static RunTargetDetection ResolveScriptDirectory(string directory, string crewScript) =>
        RunTargetDetection.Resolved(directory, new RunTarget
        {
            Kind = RunTargetKind.ScriptDirectory,
            SelectedPath = directory,
            RunPath = crewScript,
        });

    private static RunTargetDetection AmbiguousDirectory(
        string directory,
        List<string> markers,
        List<string> scripts) =>
        RunTargetDetection.Failed(
            directory,
            RunTargetCodes.AmbiguousDirectory,
            $"'{directory}' matches two run shapes at once: the multi-file YAML crew layout "
            + $"({string.Join(", ", markers)}) and a scripting entry point "
            + $"({string.Join(", ", scripts)}). "
            + "Choose which one to run — no precedence is applied.",
            [.. markers, .. scripts]);

    /// <summary>
    /// The paths identifying a multi-file YAML crew: the per-entity sub-folders when there
    /// are any, else the flat legacy triplet when all three files are present. Empty when the
    /// directory carries no YAML layout at all.
    /// </summary>
    private List<string> FindYamlLayoutMarkers(string directory)
    {
        var entityFolders = new List<string>(EntityDirectoryNames.Length);

        foreach (var name in EntityDirectoryNames)
        {
            var candidate = Path.Combine(directory, name);
            if (_probe.DirectoryExists(candidate))
                entityFolders.Add(candidate);
        }

        if (entityFolders.Count > 0)
            return entityFolders;

        var flat = FlatLayoutFileNames.Select(name => Path.Combine(directory, name)).ToList();
        return flat.TrueForAll(_probe.FileExists) ? flat : entityFolders;
    }

    /// <summary>
    /// Scripts of the directory, filtered on the real suffix: the glob is only a hint to
    /// the file system, whose pattern matching differs between platforms. Both globs can
    /// return the same file, so duplicates are dropped.
    /// </summary>
    private List<string> ListScripts(string directory) =>
        [.. ScriptSearchPatterns
            .SelectMany(pattern => _probe.EnumerateFiles(directory, pattern))
            .Where(HasScriptSuffix)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)];

    private static bool IsConventionalCrewScript(string path) =>
        string.Equals(Path.GetFileName(path), CrewScriptFileName, StringComparison.OrdinalIgnoreCase);

    private static bool HasScriptSuffix(string path) =>
        ScriptSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static string DescribeScriptPatterns() =>
        string.Join(" / ", ScriptSearchPatterns.Select(pattern => $"'{pattern}'"));

    /// <summary>True for a YAML crew file — the same rule as <c>RunCommand.IsYamlConfig</c>.</summary>
    public static bool IsYamlFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return YamlExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True for a scripting-DSL file (<c>.ork.ts</c> or <c>.js</c>).</summary>
    public static bool IsScriptFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(ScriptSuffix, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".js", StringComparison.OrdinalIgnoreCase);
    }
}
