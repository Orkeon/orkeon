using Orkeon.Studio.Run.Tests.Doubles;
using Orkeon.Studio.Run.Views;

namespace Orkeon.Studio.Run.Tests.Cli;

/// <summary>
/// The entry point and the launcher write on the streams they are given: the process console
/// is handed on by <c>Main</c> alone.
/// </summary>
public sealed class ProgramTests
{
    [Fact]
    public void ShouldAnswerOnTheGivenOutput_WhenAskedForTheVersion()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = Program.Run(["--version"], output, error, new FakeTerminalProbe());

        Assert.Equal(0, exit);
        Assert.NotEmpty(output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void ShouldAnswerOnTheGivenError_WhenTheArgumentIsUnknown()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = Program.Run(["--no-such-switch"], output, error, new FakeTerminalProbe());

        Assert.NotEqual(0, exit);
        Assert.NotEmpty(error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ShouldReportOnTheGivenError_WhenTheTerminalIsRedirected()
    {
        using var error = new StringWriter();
        var launcher = new LauncherApplication(error, new FakeTerminalProbe { OutputRedirected = true });

        var exit = launcher.Run();

        Assert.Equal(LauncherApplication.TerminalUnavailableExitCode, exit);
        Assert.Contains("needs an interactive terminal", error.ToString(), StringComparison.Ordinal);
    }
}
