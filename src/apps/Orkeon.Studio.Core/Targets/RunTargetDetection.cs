namespace Orkeon.Studio.Core.Targets;

/// <summary>Outcome of <see cref="RunTargetDetector.Detect"/>.</summary>
public enum RunTargetDetectionStatus
{
    /// <summary>One shape was recognised; <see cref="RunTargetDetection.Target"/> is set.</summary>
    Resolved,

    /// <summary>
    /// The directory holds no conventional entry point but does hold <c>*.ork.ts</c>
    /// scripts: the user picks one from <see cref="RunTargetDetection.Candidates"/>.
    /// </summary>
    NeedsSelection,

    /// <summary>Nothing runnable, or two competing shapes; see <see cref="RunTargetDetection.Error"/>.</summary>
    Failed,
}

/// <summary>
/// Something a resolved detection has to say without refusing anything: a stable code
/// (see <see cref="RunTargetCodes"/>) and its text, shown as an information line of the
/// launch validation.
/// </summary>
/// <param name="Code">Stable code, for UI keying and tests.</param>
/// <param name="Text">Human-readable text naming the paths concerned.</param>
public sealed record RunTargetNotice(string Code, string Text);

/// <summary>
/// What the detector made of a picked path: a resolved target, a list of candidates for
/// the user to choose from, or an explicit error. Ambiguity is never resolved by
/// precedence — a directory that looks like two shapes at once fails and names both. A
/// <c>crew/</c> sub-folder holding a crew is not a competing shape but the container of
/// the team's definition (STUDIO-59): it wins over whatever the root holds, and the
/// detection carries a <see cref="Notices">notice</see> naming what was set aside.
/// </summary>
public sealed record RunTargetDetection
{
    /// <summary>Outcome kind.</summary>
    public required RunTargetDetectionStatus Status { get; init; }

    /// <summary>The path that was inspected.</summary>
    public required string SelectedPath { get; init; }

    /// <summary>Set when <see cref="Status"/> is <see cref="RunTargetDetectionStatus.Resolved"/>.</summary>
    public RunTarget? Target { get; init; }

    /// <summary>
    /// The competing or selectable paths: the <c>*.ork.ts</c> scripts to choose from when
    /// selection is needed, the conflicting shapes when detection failed on ambiguity.
    /// </summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>Human-readable failure, set when <see cref="Status"/> is <see cref="RunTargetDetectionStatus.Failed"/>.</summary>
    public string? Error { get; init; }

    /// <summary>Stable failure code (see <see cref="RunTargetCodes"/>), for UI keying and tests.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// What a resolved detection has to say about the path without refusing it — the
    /// root folders a <c>crew/</c> sub-folder set aside. Empty on a failed detection.
    /// </summary>
    public IReadOnlyList<RunTargetNotice> Notices { get; init; } = [];

    /// <summary>True when a target was resolved.</summary>
    public bool IsResolved => Status == RunTargetDetectionStatus.Resolved;

    /// <summary>The same detection, carrying one more notice.</summary>
    internal RunTargetDetection WithNotice(string code, string text) =>
        this with { Notices = [.. Notices, new RunTargetNotice(code, text)] };

    internal static RunTargetDetection Resolved(string selectedPath, RunTarget target) =>
        new() { Status = RunTargetDetectionStatus.Resolved, SelectedPath = selectedPath, Target = target };

    internal static RunTargetDetection NeedsSelection(string selectedPath, IReadOnlyList<string> candidates) =>
        new()
        {
            Status = RunTargetDetectionStatus.NeedsSelection,
            SelectedPath = selectedPath,
            Candidates = candidates,
        };

    internal static RunTargetDetection Failed(
        string selectedPath,
        string code,
        string error,
        IReadOnlyList<string>? candidates = null) =>
        new()
        {
            Status = RunTargetDetectionStatus.Failed,
            SelectedPath = selectedPath,
            ErrorCode = code,
            Error = error,
            Candidates = candidates ?? [],
        };
}

/// <summary>Stable codes carried by <see cref="RunTargetDetection.ErrorCode"/>.</summary>
public static class RunTargetCodes
{
    /// <summary>No path was given.</summary>
    public const string EmptyPath = "STUDIO-TARGET-EMPTY";

    /// <summary>The path is neither an existing file nor an existing directory.</summary>
    public const string PathNotFound = "STUDIO-TARGET-MISSING";

    /// <summary>The file's extension is none the CLI runs.</summary>
    public const string UnsupportedExtension = "STUDIO-TARGET-EXTENSION";

    /// <summary>The directory looks like two shapes at once.</summary>
    public const string AmbiguousDirectory = "STUDIO-TARGET-AMBIGUOUS";

    /// <summary>
    /// The YAML layout was preferred on a directory that also holds a script: the CLI rejects
    /// such a directory outright, so the preference cannot produce a runnable command.
    /// </summary>
    public const string YamlLayoutBlockedByScript = "STUDIO-TARGET-YAML-BLOCKED";

    /// <summary>The directory holds nothing runnable.</summary>
    public const string NoCandidate = "STUDIO-TARGET-NO-CANDIDATE";

    /// <summary>
    /// Notice, not an error: the directory's <c>crew/</c> sub-folder is the crew, and the
    /// layout markers or scripts found at the root — a mount point named <c>agents</c>, a
    /// stray script — were set aside, never read as a crew.
    /// </summary>
    public const string RootShadowedByPromotedCrew = "STUDIO-TARGET-ROOT-SHADOWED";
}
