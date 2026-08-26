using System.Globalization;
using Orkeon.Studio.Core.Localization;

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
    /// True when the run path is a directory, so the launch needs a CLI that dispatches
    /// <c>orkeon run</c> on a directory rather than on a file extension. That dispatch ships
    /// since <see cref="RunTargetRequirements.MinimumCliVersion"/>; the notice a UI shows is
    /// <see cref="RunTargetRequirements.DirectoryRunNotice"/>.
    /// </summary>
    public bool RequiresDirectoryRunSupport => Kind == RunTargetKind.MultiFileCrewDirectory;

    /// <summary>
    /// The directory the CLI is launched from — always derived from <see cref="SelectedPath"/>,
    /// never from <see cref="RunPath"/>.
    /// <para>
    /// The two are not the same folder any more. Since ADR-008 the detector descends into a
    /// promoted team's <c>crew/</c>, so <see cref="RunPath"/> points one level below the folder
    /// the user picked — the folder that holds the sidecar, the <c>appsettings.json</c> and the
    /// <c>output/</c> the team writes to. Deriving the working directory from
    /// <see cref="RunPath"/> put the CLI inside <c>crew/</c>, where it found none of them. The
    /// WPF launcher was corrected and the TUI one was not; one implementation now, so they
    /// cannot disagree again.
    /// </para>
    /// </summary>
    public string? WorkingDirectory
    {
        get
        {
            if (Kind is RunTargetKind.MultiFileCrewDirectory or RunTargetKind.ScriptDirectory)
                return SelectedPath;

            var directory = Path.GetDirectoryName(SelectedPath);
            return string.IsNullOrEmpty(directory) ? null : directory;
        }
    }
}

/// <summary>Framework prerequisites a detected target may carry, as UI-ready text.</summary>
public static class RunTargetRequirements
{
    /// <summary>
    /// Oldest Orkeon release whose <c>orkeon run</c> dispatches on a crew directory. The CLI
    /// shipped alongside Studio is built from this repository, so it always satisfies it; the
    /// version matters only for a separately installed, older CLI found on <c>PATH</c>.
    /// </summary>
    public const string MinimumCliVersion = "0.9.2-beta";

    /// <summary>
    /// Shown for a multi-file crew directory, as advice rather than a warning: directory
    /// dispatch is a released capability, not a pending one.
    /// </summary>
    public const string DirectoryRunNotice =
        "This is a multi-file crew directory: running it uses 'orkeon run <directory>', which " +
        "requires Orkeon >= " + MinimumCliVersion + ". The CLI installed alongside Studio " +
        "supports this.";

    /// <summary><see cref="DirectoryRunNotice"/> resolved through a culture port (STUDIO-11).</summary>
    public static string DirectoryRunNoticeFor(IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return string.Format(
            CultureInfo.InvariantCulture,
            strings[StudioStringKeys.TargetDirectoryRunNotice],
            MinimumCliVersion);
    }
}
