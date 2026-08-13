using System.Globalization;

namespace Orkeon.Studio.Core.Targets;

/// <summary>
/// Recognises what a picked path is, using the rules the CLI and the loaders already use:
/// <list type="bullet">
///   <item><description>a file, by extension — the same test as
///   <c>RunCommand.IsYamlConfig</c> (<c>src/scripting/Orkeon.Scripting.Cli/Commands/RunCommand.cs:243-247</c>)
///   for YAML, the scripting extensions otherwise;</description></item>
///   <item><description>a directory holding an <c>agents/</c> or <c>tasks/</c> sub-folder —
///   the multi-file crew layout (<c>features/crew-multifile-directory-layout.md</c>);</description></item>
///   <item><description>a directory holding <c>crew.ork.ts</c> — the scripting convention
///   (<c>ScriptHostFacadeOptions.CrewFileName</c>), or else its <c>*.ork.ts</c> files offered
///   as candidates.</description></item>
/// </list>
/// A directory that matches two shapes at once is an error naming both: the layout loader
/// applies no precedence there and neither does Studio.
/// </summary>
public sealed class RunTargetDetector
{
    /// <summary>Conventional script entry point in a crew directory (mirrors <c>ScriptHostFacadeOptions.CrewFileName</c>).</summary>
    public const string CrewScriptFileName = "crew.ork.ts";

    /// <summary>Per-entity agents sub-folder of the multi-file crew layout.</summary>
    public const string AgentsDirectoryName = "agents";

    /// <summary>Per-entity tasks sub-folder of the multi-file crew layout.</summary>
    public const string TasksDirectoryName = "tasks";

    /// <summary>Suffix of a scripting-DSL crew file.</summary>
    public const string ScriptSuffix = ".ork.ts";

    /// <summary>Glob used to list the scripts of a directory.</summary>
    private const string ScriptSearchPattern = "*" + ScriptSuffix;

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
    /// directory is an error naming both candidates; set to
    /// <see cref="RunTargetKind.MultiFileCrewDirectory"/> or
    /// <see cref="RunTargetKind.ScriptDirectory"/>, it is the user's explicit answer to
    /// that error and resolves the target.
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

    private RunTargetDetection DetectDirectory(string directory, RunTargetKind? preferredKind)
    {
        var markers = FindMultiFileMarkers(directory);
        var crewScript = Path.Combine(directory, CrewScriptFileName);
        var hasCrewScript = _probe.FileExists(crewScript);

        if (markers.Count > 0 && hasCrewScript)
        {
            return preferredKind switch
            {
                RunTargetKind.MultiFileCrewDirectory => ResolveMultiFile(directory, markers),
                RunTargetKind.ScriptDirectory => ResolveScriptDirectory(directory, crewScript),
                _ => AmbiguousDirectory(directory, markers, crewScript),
            };
        }

        if (markers.Count > 0)
            return ResolveMultiFile(directory, markers);

        if (hasCrewScript)
            return ResolveScriptDirectory(directory, crewScript);

        var scripts = ListScripts(directory);
        if (scripts.Count > 0)
            return RunTargetDetection.NeedsSelection(directory, scripts);

        return RunTargetDetection.Failed(
            directory,
            RunTargetCodes.NoCandidate,
            $"'{directory}' holds no crew definition: no '{AgentsDirectoryName}/' or '{TasksDirectoryName}/' "
            + $"sub-folder, no '{CrewScriptFileName}', no '{ScriptSearchPattern}' file. "
            + "Pick a .yaml or .ork.ts file inside it instead.");
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
        string crewScript)
    {
        var candidates = new List<string>(markers.Count + 1);
        candidates.AddRange(markers);
        candidates.Add(crewScript);

        return RunTargetDetection.Failed(
            directory,
            RunTargetCodes.AmbiguousDirectory,
            $"'{directory}' matches two run shapes at once: the multi-file crew layout "
            + $"({string.Join(", ", markers)}) and the script entry point ({crewScript}). "
            + "Choose which one to run — no precedence is applied.",
            candidates);
    }

    private List<string> FindMultiFileMarkers(string directory)
    {
        var markers = new List<string>(2);

        foreach (var name in new[] { AgentsDirectoryName, TasksDirectoryName })
        {
            var candidate = Path.Combine(directory, name);
            if (_probe.DirectoryExists(candidate))
                markers.Add(candidate);
        }

        return markers;
    }

    /// <summary>
    /// Scripts of the directory, filtered on the real suffix: the glob is only a hint to
    /// the file system, whose pattern matching differs between platforms.
    /// </summary>
    private List<string> ListScripts(string directory) =>
        [.. _probe.EnumerateFiles(directory, ScriptSearchPattern)
            .Where(file => file.EndsWith(ScriptSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)];

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
