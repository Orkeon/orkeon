using System.Globalization;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Launch;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// The families a wizard failure falls into (STUDIO-13). One family, one novice sentence,
/// one set of ways out; the classification reads the run outcome, the engine's own
/// <c>error</c> event and the exit code — never the text of stderr.
/// </summary>
public enum WizardFailureKind
{
    /// <summary>
    /// The run never started: the locator found no <c>orkeon</c> binary on this machine
    /// (<see cref="Orkeon.Studio.Core.Process.RunOutcome.NotStarted"/>).
    /// </summary>
    EngineMissing,

    /// <summary>
    /// The engine refused what it was given and said so on its own channel: an <c>error</c>
    /// event with <c>recoverable: false</c> (a <c>FORGE-*</c> code) — the configuration, the
    /// brief, or a stage it could not complete.
    /// </summary>
    ConfigRefused,

    /// <summary>The child exited non-zero without an unrecoverable engine error — with or without stderr.</summary>
    EngineStopped,

    /// <summary>An exception escaped the launch before the engine could answer.</summary>
    Unknown,

    /// <summary>Step 4: <c>forge promote</c> ended without a <c>promoted</c> event — nothing landed on disk.</summary>
    PromoteRefused,
}

/// <summary>
/// One failure of "Compose the team" (or of the save), as the card under the stepper shows
/// it (STUDIO-13, D-03). The screen used to keep the reason on a grey one-line status, or
/// nowhere: a novice whose CLI is missing clicked, saw a spinner, then step 1 again.
/// </summary>
/// <param name="Kind">The family — drives the sentence and the buttons.</param>
/// <param name="Headline">The novice sentence, localized.</param>
/// <param name="Detail">The raw technical text — the CLI's own words, never translated (STUDIO-11).</param>
/// <param name="CommandLine">The engine invocation that failed, replayable in a terminal.</param>
/// <param name="ExitCode">The child's exit code; null when it never started.</param>
/// <param name="Stderr">The WHOLE stderr (D-04), not the last line; empty when nothing was printed.</param>
/// <param name="EngineError">The engine's own <c>error</c> event, when it emitted one.</param>
/// <param name="Journal">Everything the run printed, in order — the technical journal as the expert sees it.</param>
public sealed record WizardFailure(
    WizardFailureKind Kind,
    string Headline,
    string Detail,
    string? CommandLine,
    int? ExitCode,
    string Stderr,
    ForgeErrorInfo? EngineError,
    string Journal = "")
{
    /// <summary>Whether the raw detail zone has anything to show.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>
    /// The copyable report: the command line, the exit code, the engine's error, the detail the
    /// card shows and the whole journal. Plain text, English labels — it is meant to be pasted
    /// to whoever will read the technical part, and WPF text blocks are not selectable.
    /// </summary>
    public string BuildReport()
    {
        var lines = new List<string>
        {
            CommandLine is { Length: > 0 }
                ? CommandLine
                : CommandLineDisplay.Format([ForgeArgumentsBuilder.ForgeVerb]),
            ExitCode is { } exitCode
                ? string.Create(CultureInfo.InvariantCulture, $"exit {exitCode}")
                : "exit: none — the engine never started",
        };

        if (EngineError is { } error)
            lines.Add(error.Message.Length > 0 ? $"{error.Code}: {error.Message}" : error.Code);

        if (Detail.Length > 0)
        {
            lines.Add("");
            lines.Add(Detail);
        }

        if (Journal.Length > 0 && !string.Equals(Journal, Detail, StringComparison.Ordinal))
        {
            lines.Add("");
            lines.Add("--- journal ---");
            lines.Add(Journal);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
