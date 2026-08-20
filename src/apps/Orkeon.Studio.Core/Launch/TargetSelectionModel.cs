using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Launch;

/// <summary>What the target picker is currently showing.</summary>
public enum TargetSelectionState
{
    /// <summary>Nothing picked yet.</summary>
    Empty,

    /// <summary>A shape was recognised and the launch can be prepared.</summary>
    Resolved,

    /// <summary>The directory holds several scripts; the user picks one.</summary>
    NeedsSelection,

    /// <summary>Nothing runnable, or two competing shapes.</summary>
    Failed,
}

/// <summary>
/// The target picker's state: it holds the picked path and whatever
/// <see cref="RunTargetDetector"/> made of it, and turns that into the labels the screen
/// shows. It decides nothing about the shapes itself — errors, including the ambiguity
/// error that names both candidates, are surfaced verbatim.
/// </summary>
public sealed class TargetSelectionModel
{
    private readonly RunTargetDetector _detector;

    /// <summary>Creates a picker over <paramref name="detector"/> (defaults to the real disk).</summary>
    public TargetSelectionModel(RunTargetDetector? detector = null) =>
        _detector = detector ?? new RunTargetDetector();

    /// <summary>The path currently in the picker's field.</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>The detector's last verdict; null before anything was picked.</summary>
    public RunTargetDetection? Detection { get; private set; }

    /// <summary>The resolved target, when there is one.</summary>
    public RunTarget? Target => Detection?.Target;

    /// <summary>Current state of the picker.</summary>
    public TargetSelectionState State => Detection?.Status switch
    {
        RunTargetDetectionStatus.Resolved => TargetSelectionState.Resolved,
        RunTargetDetectionStatus.NeedsSelection => TargetSelectionState.NeedsSelection,
        RunTargetDetectionStatus.Failed => TargetSelectionState.Failed,
        _ => TargetSelectionState.Empty,
    };

    /// <summary>Scripts to choose from, or the conflicting shapes of an ambiguous directory.</summary>
    public IReadOnlyList<string> Candidates => Detection?.Candidates ?? [];

    /// <summary>The detector's own failure text — shown as-is, since it names the candidates.</summary>
    public string? Error => Detection?.Status == RunTargetDetectionStatus.Failed ? Detection.Error : null;

    /// <summary>Stable code of the failure, for keying the remediation the screen offers.</summary>
    public string? ErrorCode => Detection?.ErrorCode;

    /// <summary>
    /// True when the failure is one the user resolves by choosing a shape. Preferring the YAML
    /// layout of a contested directory counts: the CLI rejects it, so that answer leaves the
    /// choice open rather than closing it.
    /// </summary>
    public bool NeedsShapeChoice =>
        ErrorCode is RunTargetCodes.AmbiguousDirectory or RunTargetCodes.YamlLayoutBlockedByScript;

    /// <summary>A launch can be prepared.</summary>
    public bool IsResolved => Target is not null;

    /// <summary>
    /// The framework prerequisite of the resolved target, or null. Set for a multi-file crew
    /// directory, whose launch relies on <c>orkeon run &lt;directory&gt;</c>.
    /// </summary>
    public string? FrameworkRequirement =>
        Target is { RequiresDirectoryRunSupport: true } ? RunTargetRequirements.DirectoryRunNotice : null;

    /// <summary>The one-line description of the recognised shape shown next to the field.</summary>
    public string ShapeDescription => Target is null
        ? "No target selected."
        : DescribeKind(Target.Kind);

    /// <summary>Human-readable name of a recognised shape.</summary>
    public static string DescribeKind(RunTargetKind kind) => kind switch
    {
        RunTargetKind.YamlFile => "YAML crew file",
        RunTargetKind.ScriptFile => "Scripting DSL file",
        RunTargetKind.MultiFileCrewDirectory => "Multi-file crew directory",
        RunTargetKind.ScriptDirectory => $"Script directory ({RunTargetDetector.CrewScriptFileName})",
        _ => kind.ToString(),
    };

    /// <summary>Inspects <paramref name="path"/> and adopts the verdict.</summary>
    public RunTargetDetection Select(string? path)
    {
        SelectedPath = path;
        Detection = _detector.Detect(path);
        return Detection;
    }

    /// <summary>
    /// Adopts one of the <c>*.ork.ts</c> scripts offered by
    /// <see cref="TargetSelectionState.NeedsSelection"/>: the file is re-detected, so the
    /// resolved target is the ordinary script-file shape.
    /// </summary>
    public RunTargetDetection SelectCandidate(string candidatePath) => Select(candidatePath);

    /// <summary>
    /// Answers the ambiguity error by naming the shape to run. The directory is re-detected
    /// with that preference, which is the only thing that resolves it — no precedence is
    /// applied on the user's behalf.
    /// </summary>
    public RunTargetDetection ResolveShape(RunTargetKind kind)
    {
        Detection = _detector.Detect(SelectedPath, kind);
        return Detection;
    }

    /// <summary>Forgets the current selection.</summary>
    public void Clear()
    {
        SelectedPath = null;
        Detection = null;
    }
}
