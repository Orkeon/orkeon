using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// Turns a finished run into the single line a front-end's status bar shows: whether the run
/// was a dry run (a verdict) or a real one (a code plus its meaning). The English overloads
/// carry the engine's own sentence (<see cref="OrkeonExitCodes.Describe"/>); the localized ones
/// say what the outcome means in the user's language, from the outcome alone, and leave the
/// engine's sentence to <see cref="ProcessRunResult.Description"/>.
/// <para>
/// It lives in Core because the verdict is part of what a dry run <em>is</em>: a launcher that
/// only echoed the exit-code description would leave the user to work out for themselves
/// whether <c>--validate</c> passed.
/// </para>
/// </summary>
public static class LaunchOutcomeFormatter
{
    /// <summary>Verdict of a dry run the CLI accepted.</summary>
    public const string ValidationOk = "VALIDATION OK";

    /// <summary>Verdict of a dry run the CLI rejected, or that never started.</summary>
    public const string ValidationFailed = "VALIDATION FAILED";

    /// <summary>Describes a real run: its exit code and what that code means (English).</summary>
    public static string DescribeRun(ProcessRunResult result)
        => DescribeRunWithEngineSentence(result, EnglishStudioStrings.Instance);

    /// <summary>
    /// Describes a real run in the given culture port (STUDIO-11): the code, then what the
    /// outcome means, localized — never the engine's English sentence, which stays in
    /// <see cref="ProcessRunResult.Description"/> for the expert.
    /// </summary>
    public static string DescribeRun(ProcessRunResult result, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strings);

        return result.Outcome == RunOutcome.NotStarted
            ? strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.LaunchNothingRan, strings[StudioStringKeys.LaunchExitNotStarted])
            : strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.LaunchExitCode, result.ExitCode, strings[OutcomeKey(result.Outcome)]);
    }

    /// <summary>Describes a dry run (<c>--validate</c>) as the verdict the spec asks for (English).</summary>
    public static string DescribeValidation(ProcessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return $"{Verdict(result)} — {DescribeRunWithEngineSentence(result, EnglishStudioStrings.Instance)}";
    }

    /// <summary>
    /// Describes a dry run in the given culture port. The verdict words themselves
    /// (<see cref="ValidationOk"/>/<see cref="ValidationFailed"/>) are the CLI's
    /// contract and stay untranslated by design.
    /// </summary>
    public static string DescribeValidation(ProcessRunResult result, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(result);

        return $"{Verdict(result)} — {DescribeRun(result, strings)}";
    }

    /// <summary>Describes either kind of run (English).</summary>
    public static string Describe(ProcessRunResult result, bool dryRun) =>
        dryRun ? DescribeValidation(result) : DescribeRun(result);

    /// <summary>Describes either kind of run in the given culture port.</summary>
    public static string Describe(ProcessRunResult result, bool dryRun, IStudioStrings strings) =>
        dryRun ? DescribeValidation(result, strings) : DescribeRun(result, strings);

    /// <summary>True when the outcome should be painted as a success.</summary>
    public static bool IsSuccess(ProcessRunResult? result) => result?.Outcome == RunOutcome.Success;

    private static string Verdict(ProcessRunResult result) =>
        result.Outcome == RunOutcome.Success ? ValidationOk : ValidationFailed;

    /// <summary>The line with the engine's own sentence, as the English launcher prints it.</summary>
    private static string DescribeRunWithEngineSentence(ProcessRunResult result, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome == RunOutcome.NotStarted
            ? strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.LaunchNothingRan, result.Description)
            : strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.LaunchExitCode, result.ExitCode, result.Description);
    }

    private static string OutcomeKey(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Success => StudioStringKeys.LaunchExitSuccess,
        RunOutcome.ScriptError => StudioStringKeys.LaunchExitScriptError,
        RunOutcome.RuntimeError => StudioStringKeys.LaunchExitRuntimeError,
        RunOutcome.Cancelled => StudioStringKeys.LaunchExitCancelled,
        _ => StudioStringKeys.LaunchExitUnknown,
    };
}
