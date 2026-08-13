using Orkeon.Studio.Run.Cli;

namespace Orkeon.Studio.Run.Tests.Cli;

/// <summary>
/// The launcher's contract with a machine that has no terminal: <c>--version</c> and
/// <c>--help</c> answer on stdout with exit code 0, and nothing else is claimed so the UI
/// still opens for a bare invocation.
/// </summary>
public class HeadlessCommandLineTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("--VERSION")]
    public void Version_flag_prints_the_tool_name_and_version(string flag)
    {
        Assert.True(HeadlessCommandLine.TryHandle([flag], out var response));

        Assert.Equal(0, response.ExitCode);
        Assert.StartsWith(StudioRunInfo.ToolName, response.Text, StringComparison.Ordinal);
        Assert.Contains(StudioRunInfo.Version, response.Text, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, response.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Help_flag_prints_the_usage(string flag)
    {
        Assert.True(HeadlessCommandLine.TryHandle([flag], out var response));

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Usage:", response.Text, StringComparison.Ordinal);
        Assert.Contains("--validate", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_is_recognised_after_other_arguments()
    {
        Assert.True(HeadlessCommandLine.TryHandle(["/crews/demo.yaml", "--version"], out var response));
        Assert.Equal(StudioRunInfo.VersionLine + Environment.NewLine, response.Text);
    }

    [Fact]
    public void No_arguments_opens_the_ui()
    {
        Assert.False(HeadlessCommandLine.TryHandle([], out var response));
        Assert.Null(response);
    }

    [Fact]
    public void Null_arguments_open_the_ui()
    {
        Assert.False(HeadlessCommandLine.TryHandle(null, out _));
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("/crews/demo/crew.yaml")]
    public void An_unknown_argument_is_a_usage_error_on_stderr(string argument)
    {
        // Opening a full-screen UI on a typo would hide it, and the launcher takes no
        // arguments of its own — the crew is picked on screen.
        Assert.True(HeadlessCommandLine.TryHandle([argument], out var response));

        Assert.Equal(HeadlessCommandLine.UsageExitCode, response.ExitCode);
        Assert.True(response.IsError);
        Assert.Contains(argument, response.Text, StringComparison.Ordinal);
        Assert.Contains("--help", response.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_flag_wins_over_an_unknown_one()
    {
        Assert.True(HeadlessCommandLine.TryHandle(["--frobnicate", "--version"], out var response));

        Assert.Equal(0, response.ExitCode);
        Assert.False(response.IsError);
    }

    [Fact]
    public void Answered_questions_go_to_stdout()
    {
        Assert.True(HeadlessCommandLine.TryHandle(["--version"], out var version));
        Assert.True(HeadlessCommandLine.TryHandle(["--help"], out var help));

        Assert.False(version.IsError);
        Assert.False(help.IsError);
    }

    [Fact]
    public void Version_carries_no_build_metadata()
    {
        Assert.DoesNotContain("+", StudioRunInfo.Version, StringComparison.Ordinal);
    }
}
