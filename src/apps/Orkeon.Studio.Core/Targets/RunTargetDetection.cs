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
/// What the detector made of a picked path: a resolved target, a list of candidates for
/// the user to choose from, or an explicit error. Ambiguity is never resolved by
/// precedence — a directory that looks like two shapes at once fails and names both.
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

    /// <summary>True when a target was resolved.</summary>
    public bool IsResolved => Status == RunTargetDetectionStatus.Resolved;

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

    /// <summary>The directory holds nothing runnable.</summary>
    public const string NoCandidate = "STUDIO-TARGET-NO-CANDIDATE";
}
