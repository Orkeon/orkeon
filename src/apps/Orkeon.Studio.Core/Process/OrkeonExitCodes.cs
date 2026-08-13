using System.Globalization;

namespace Orkeon.Studio.Core.Process;

/// <summary>How a finished (or never-started) run should be presented to the user.</summary>
public enum RunOutcome
{
    /// <summary>The process could not be started at all — no exit code exists.</summary>
    NotStarted,

    /// <summary>Exit code 0.</summary>
    Success,

    /// <summary>Exit code 1 — the script or the configuration was rejected.</summary>
    ScriptError,

    /// <summary>Exit code 2 — the run started and failed at runtime.</summary>
    RuntimeError,

    /// <summary>Exit code 130 — interrupted (the CLI's SIGINT contract).</summary>
    Cancelled,

    /// <summary>Any other exit code. Never silent: the raw code is carried in the description.</summary>
    Unknown,
}

/// <summary>
/// The exit-code contract of the <c>orkeon</c> CLI, mirrored from
/// <c>Orkeon.Scripting.Cli.Program</c> (<c>ExitOk</c>/<c>ExitScriptError</c>/
/// <c>ExitRuntimeError</c>/<c>ExitCancelled</c>). Studio does not reference the CLI
/// assembly, so the four values are restated here and asserted by tests.
/// </summary>
public static class OrkeonExitCodes
{
    /// <summary>Everything ran.</summary>
    public const int Success = 0;

    /// <summary>Script or configuration error.</summary>
    public const int ScriptError = 1;

    /// <summary>Runtime error during the run.</summary>
    public const int RuntimeError = 2;

    /// <summary>Interrupted — the code the CLI returns after SIGINT/SIGTERM.</summary>
    public const int Cancelled = 130;

    /// <summary>Maps a raw exit code onto its outcome; anything unmapped becomes <see cref="RunOutcome.Unknown"/>.</summary>
    public static RunOutcome Classify(int exitCode) => exitCode switch
    {
        Success => RunOutcome.Success,
        ScriptError => RunOutcome.ScriptError,
        RuntimeError => RunOutcome.RuntimeError,
        Cancelled => RunOutcome.Cancelled,
        _ => RunOutcome.Unknown,
    };

    /// <summary>A one-line, user-facing explanation of a raw exit code.</summary>
    public static string Describe(int exitCode) => exitCode switch
    {
        Success => "Completed successfully (exit code 0).",
        ScriptError => "Script or configuration error (exit code 1) — check the crew file and the settings it resolved.",
        RuntimeError => "Runtime error (exit code 2) — the run started but failed; the last stderr lines say why.",
        Cancelled => "Interrupted (exit code 130).",
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"Unexpected exit code {exitCode} — orkeon ended in a way Studio does not recognise; check the output above."),
    };
}
