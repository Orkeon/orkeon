using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The status line: the CLI's four exit codes read back as sentences, and the dry-run
/// verdict the spec asks for by name.
/// </summary>
public class LaunchOutcomeFormatterTests
{
    private static ProcessRunResult Exited(int exitCode) =>
        ProcessRunResult.FromExitCode(exitCode, TimeSpan.FromSeconds(1));

    [Theory]
    [InlineData(OrkeonExitCodes.Success)]
    [InlineData(OrkeonExitCodes.ScriptError)]
    [InlineData(OrkeonExitCodes.RuntimeError)]
    [InlineData(OrkeonExitCodes.Cancelled)]
    public void A_run_is_described_by_its_exit_code(int exitCode)
    {
        var text = LaunchOutcomeFormatter.DescribeRun(Exited(exitCode));

        Assert.Contains($"Exit code {exitCode}", text, StringComparison.Ordinal);
        Assert.Contains(OrkeonExitCodes.Describe(exitCode), text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_interrupted_run_shows_130()
    {
        var cancelled = ProcessRunResult.FromCancellation(
            OrkeonExitCodes.Cancelled,
            ProcessTerminationOutcome.Of(ProcessTerminationMode.StoppedBySignal),
            TimeSpan.FromSeconds(1));

        Assert.Contains("Exit code 130", LaunchOutcomeFormatter.DescribeRun(cancelled), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_never_started_shows_the_reason_and_no_code()
    {
        var text = LaunchOutcomeFormatter.DescribeRun(ProcessRunResult.NotStarted("`orkeon` was not found."));

        Assert.Contains("`orkeon` was not found.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exit code", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrkeonExitCodes.Success, "Exit code 0 — run completed successfully")]
    [InlineData(OrkeonExitCodes.ScriptError, "Exit code 1 — script or settings error: check the crew file and the settings it uses")]
    [InlineData(OrkeonExitCodes.RuntimeError, "Exit code 2 — runtime error: the last log lines say why")]
    [InlineData(OrkeonExitCodes.Cancelled, "Exit code 130 — run interrupted")]
    [InlineData(42, "Exit code 42 — unexpected end: check the output above")]
    public void The_localized_overload_says_the_outcome_in_the_ports_words_never_the_engines(int exitCode, string expected)
    {
        var result = Exited(exitCode);

        var text = LaunchOutcomeFormatter.DescribeRun(result, EnglishStudioStrings.Instance);

        Assert.Equal(expected, text);
        Assert.DoesNotContain(result.Description, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_localized_overload_says_a_run_never_started_without_the_engines_reason()
    {
        var text = LaunchOutcomeFormatter.DescribeRun(
            ProcessRunResult.NotStarted("`orkeon` was not found."), EnglishStudioStrings.Instance);

        Assert.Equal("Nothing ran — the engine did not start", text);
    }

    [Fact]
    public void The_english_launcher_keeps_the_engines_sentence()
    {
        Assert.Equal(
            "Exit code 0 — Completed successfully (exit code 0).",
            LaunchOutcomeFormatter.DescribeRun(Exited(OrkeonExitCodes.Success)));
        Assert.Equal(
            "VALIDATION OK — Exit code 0 — Completed successfully (exit code 0).",
            LaunchOutcomeFormatter.DescribeValidation(Exited(OrkeonExitCodes.Success)));
    }

    [Fact]
    public void A_dry_run_that_succeeded_is_validation_ok()
    {
        var text = LaunchOutcomeFormatter.Describe(Exited(OrkeonExitCodes.Success), dryRun: true);

        Assert.StartsWith(LaunchOutcomeFormatter.ValidationOk, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrkeonExitCodes.ScriptError)]
    [InlineData(OrkeonExitCodes.RuntimeError)]
    [InlineData(OrkeonExitCodes.Cancelled)]
    public void Any_other_dry_run_outcome_is_validation_failed(int exitCode)
    {
        var text = LaunchOutcomeFormatter.Describe(Exited(exitCode), dryRun: true);

        Assert.StartsWith(LaunchOutcomeFormatter.ValidationFailed, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_run_that_never_started_is_validation_failed()
    {
        var text = LaunchOutcomeFormatter.DescribeValidation(ProcessRunResult.NotStarted("no binary"));

        Assert.StartsWith(LaunchOutcomeFormatter.ValidationFailed, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_exit_code_zero_reads_as_a_success()
    {
        Assert.True(LaunchOutcomeFormatter.IsSuccess(Exited(OrkeonExitCodes.Success)));
        Assert.False(LaunchOutcomeFormatter.IsSuccess(Exited(OrkeonExitCodes.RuntimeError)));
        Assert.False(LaunchOutcomeFormatter.IsSuccess(null));
    }
}
