using System.Globalization;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Run.Launcher;

/// <summary>
/// Turns a finished run into the single line the launcher's status bar shows. The exit-code
/// wording itself comes from <see cref="OrkeonExitCodes"/> — this only decides whether the
/// run was a dry run (a verdict) or a real one (a code plus its meaning).
/// </summary>
internal static class LaunchOutcomeFormatter
{
    /// <summary>Verdict of a dry run the CLI accepted.</summary>
    public const string ValidationOk = "VALIDATION OK";

    /// <summary>Verdict of a dry run the CLI rejected, or that never started.</summary>
    public const string ValidationFailed = "VALIDATION FAILED";

    /// <summary>Describes a real run: its exit code and what that code means.</summary>
    public static string DescribeRun(ProcessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Outcome == RunOutcome.NotStarted
            ? $"Nothing ran — {result.Description}"
            : string.Create(CultureInfo.InvariantCulture, $"Exit code {result.ExitCode} — {result.Description}");
    }

    /// <summary>Describes a dry run (<c>--validate</c>) as the verdict the spec asks for.</summary>
    public static string DescribeValidation(ProcessRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var verdict = result.Outcome == RunOutcome.Success ? ValidationOk : ValidationFailed;
        return $"{verdict} — {DescribeRun(result)}";
    }

    /// <summary>Describes either kind of run.</summary>
    public static string Describe(ProcessRunResult result, bool dryRun) =>
        dryRun ? DescribeValidation(result) : DescribeRun(result);

    /// <summary>True when the outcome should be painted as a success.</summary>
    public static bool IsSuccess(ProcessRunResult? result) => result?.Outcome == RunOutcome.Success;
}
