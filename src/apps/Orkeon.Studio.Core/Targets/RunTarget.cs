namespace Orkeon.Studio.Core.Targets;

/// <summary>
/// The shapes <c>orkeon run</c> can be pointed at, as recognised by
/// <see cref="RunTargetDetector"/>. The two file kinds are the same "file" form of the
/// spec split by dialect, because the dialect decides which CLI options apply.
/// </summary>
public enum RunTargetKind
{
    /// <summary>A <c>.yaml</c>/<c>.yml</c> crew file, run as-is.</summary>
    YamlFile,

    /// <summary>A <c>.ork.ts</c>/<c>.js</c> script file, run as-is.</summary>
    ScriptFile,

    /// <summary>
    /// A directory holding a multi-file crew (an <c>agents/</c> or <c>tasks/</c>
    /// sub-folder). The directory itself is the run path — see
    /// <see cref="RunTarget.RequiresDirectoryRunSupport"/>.
    /// </summary>
    MultiFileCrewDirectory,

    /// <summary>A directory holding the conventional <c>crew.ork.ts</c> entry point.</summary>
    ScriptDirectory,
}

/// <summary>Which family of CLI options a target accepts.</summary>
public enum RunTargetDialect
{
    /// <summary>YAML crew: <c>-V KEY=VALUE</c> and <c>--initial-context</c> apply.</summary>
    Yaml,

    /// <summary>Scripting DSL: <c>--inputs</c> and <c>--inputs-file</c> apply.</summary>
    Script,
}

/// <summary>
/// A resolved run target: the shape that was recognised, the path the user picked, and
/// the path that goes on the <c>orkeon run</c> command line (the two differ for a
/// directory whose <c>crew.ork.ts</c> was resolved).
/// </summary>
public sealed record RunTarget
{
    /// <summary>Recognised shape.</summary>
    public required RunTargetKind Kind { get; init; }

    /// <summary>The path the user picked (a file, or a directory).</summary>
    public required string SelectedPath { get; init; }

    /// <summary>The path passed to <c>orkeon run</c>.</summary>
    public required string RunPath { get; init; }

    /// <summary>
    /// For <see cref="RunTargetKind.MultiFileCrewDirectory"/>, the marker sub-folders that
    /// identified the layout (<c>agents/</c>, <c>tasks/</c>); empty for the other kinds.
    /// </summary>
    public IReadOnlyList<string> Markers { get; init; } = [];

    /// <summary>Option family this target accepts.</summary>
    public RunTargetDialect Dialect => Kind is RunTargetKind.YamlFile or RunTargetKind.MultiFileCrewDirectory
        ? RunTargetDialect.Yaml
        : RunTargetDialect.Script;

    /// <summary>
    /// True when running this target needs a CLI that dispatches on a directory. The
    /// current <c>RunCommand</c> dispatches on file extension only, so a UI must warn
    /// (see <see cref="RunTargetRequirements.DirectoryRunNotice"/>) instead of pretending
    /// the launch will work.
    /// </summary>
    public bool RequiresDirectoryRunSupport => Kind == RunTargetKind.MultiFileCrewDirectory;
}

/// <summary>Framework prerequisites a detected target may carry, as UI-ready text.</summary>
public static class RunTargetRequirements
{
    /// <summary>
    /// Shown for a multi-file crew directory: the CLI dispatches <c>orkeon run</c> on the
    /// file extension, so a directory target only works once directory dispatch ships.
    /// The UI prefixes it with the minimum version once that release is known.
    /// </summary>
    public const string DirectoryRunNotice =
        "This is a multi-file crew directory: running it requires 'orkeon run <directory>', " +
        "which the installed CLI may not support yet — it currently dispatches on the file " +
        "extension only. Requires a newer Orkeon CLI.";
}
