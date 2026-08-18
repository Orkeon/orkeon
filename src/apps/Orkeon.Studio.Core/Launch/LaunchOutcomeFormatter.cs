using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// Turns a finished run into the single line a front-end's status bar shows. The exit-code
/// wording itself comes from <see cref="OrkeonExitCodes"/> — this only decides whether the
/// run was a dry run (a verdict) or a real one (a code plus its meaning).
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
        => DescribeRun(result, EnglishStudioStrings.Instance);

    /// <summary>Describes a real run in the given culture port (STUDIO-11).</summary>
    public static string DescribeRun(ProcessRunResult result, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strings);

        return result.Outcome == RunOutcome.NotStarted
            ? string.Format(CultureInfo.InvariantCulture, strings[StudioStringKeys.LaunchNothingRan], result.Description)
            : string.Format(CultureInfo.InvariantCulture, strings[StudioStringKeys.LaunchExitCode], result.ExitCode, result.Description);
    }

    /// <summary>Describes a dry run (<c>--validate</c>) as the verdict the spec asks for (English).</summary>
    public static string DescribeValidation(ProcessRunResult result)
        => DescribeValidation(result, EnglishStudioStrings.Instance);

    /// <summary>
    /// Describes a dry run in the given culture port. The verdict words themselves
    /// (<see cref="ValidationOk"/>/<see cref="ValidationFailed"/>) are the CLI's
    /// contract and stay untranslated by design.
    /// </summary>
    public static string DescribeValidation(ProcessRunResult result, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(result);

        var verdict = result.Outcome == RunOutcome.Success ? ValidationOk : ValidationFailed;
        return $"{verdict} — {DescribeRun(result, strings)}";
    }

    /// <summary>Describes either kind of run (English).</summary>
    public static string Describe(ProcessRunResult result, bool dryRun) =>
        dryRun ? DescribeValidation(result) : DescribeRun(result);

    /// <summary>Describes either kind of run in the given culture port.</summary>
    public static string Describe(ProcessRunResult result, bool dryRun, IStudioStrings strings) =>
        dryRun ? DescribeValidation(result, strings) : DescribeRun(result, strings);

    /// <summary>True when the outcome should be painted as a success.</summary>
    public static bool IsSuccess(ProcessRunResult? result) => result?.Outcome == RunOutcome.Success;
}
