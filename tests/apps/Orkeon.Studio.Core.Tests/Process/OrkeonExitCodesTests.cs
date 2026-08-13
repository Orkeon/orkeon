using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// The CLI's exit-code contract, as Studio restates it. These values are the interface
/// between the two programs; a change on either side must break a test here.
/// </summary>
public sealed class OrkeonExitCodesTests
{
    [Fact]
    public void The_four_documented_codes_have_the_documented_values()
    {
        Assert.Equal(0, OrkeonExitCodes.Success);
        Assert.Equal(1, OrkeonExitCodes.ScriptError);
        Assert.Equal(2, OrkeonExitCodes.RuntimeError);
        Assert.Equal(130, OrkeonExitCodes.Cancelled);
    }

    [Theory]
    [InlineData(0, RunOutcome.Success)]
    [InlineData(1, RunOutcome.ScriptError)]
    [InlineData(2, RunOutcome.RuntimeError)]
    [InlineData(130, RunOutcome.Cancelled)]
    public void Each_documented_code_maps_onto_its_outcome(int exitCode, RunOutcome expected)
    {
        Assert.Equal(expected, OrkeonExitCodes.Classify(exitCode));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(139)]
    public void An_unknown_code_is_unknown_and_says_so(int exitCode)
    {
        Assert.Equal(RunOutcome.Unknown, OrkeonExitCodes.Classify(exitCode));

        // Not silent: the raw code has to reach the user, since nothing else explains it.
        var description = OrkeonExitCodes.Describe(exitCode);
        Assert.Contains(exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), description, StringComparison.Ordinal);
        Assert.Contains("Unexpected", description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(130)]
    public void Every_documented_code_has_a_non_empty_description(int exitCode)
    {
        Assert.False(string.IsNullOrWhiteSpace(OrkeonExitCodes.Describe(exitCode)));
    }

    [Fact]
    public void A_completed_run_carries_the_raw_code_unchanged()
    {
        var result = ProcessRunResult.FromExitCode(2, TimeSpan.FromSeconds(3));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(2, result.RawExitCode);
        Assert.Equal(RunOutcome.RuntimeError, result.Outcome);
        Assert.Equal(ProcessTerminationMode.Exited, result.Termination);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public void A_killed_run_is_reported_as_interrupted_while_keeping_the_raw_code()
    {
        // The OS reports a signal death (137 for SIGKILL); the user is shown the CLI's own
        // interrupted code, because "I cancelled it" is what actually happened.
        var result = ProcessRunResult.FromCancellation(137, ProcessTerminationMode.Killed, TimeSpan.FromSeconds(1));

        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(137, result.RawExitCode);
        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.True(result.WasCancelled);
        Assert.Contains("grace period", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_never_started_has_no_exit_code_and_carries_the_reason()
    {
        var result = ProcessRunResult.NotStarted("orkeon was not found.");

        Assert.Equal(RunOutcome.NotStarted, result.Outcome);
        Assert.Equal(ProcessTerminationMode.NotStarted, result.Termination);
        Assert.Equal(-1, result.RawExitCode);
        Assert.Equal("orkeon was not found.", result.Description);
    }
}
