using Orkeon.Studio.Config.Cli;
using Orkeon.Studio.Config.Tests.Doubles;

namespace Orkeon.Studio.Config.Tests.Cli;

/// <summary>
/// The entry point writes on the streams it is given: the process console is handed on by
/// <c>Main</c> alone.
/// </summary>
public sealed class ProgramTests
{
    [Fact]
    public void ShouldWriteTheVersionOnTheGivenOutput_WhenAskedForTheVersion()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = Program.Run(["--version"], output, error, new FakeTerminalProbe());

        Assert.Equal(0, exit);
        Assert.Equal(StudioVersion.Line + Environment.NewLine, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void ShouldWriteTheUsageOnTheGivenError_WhenTheCommandLineIsWrong()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = Program.Run(["--no-such-switch"], output, error, new FakeTerminalProbe());

        Assert.Equal(Program.ExitUsage, exit);
        Assert.Contains(StartupOptions.UsageLine, error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void ShouldReportOnTheGivenError_WhenTheTerminalIsRedirected()
    {
        using var error = new StringWriter();
        var app = new StudioConfigApp(error, new FakeTerminalProbe { InputRedirected = true });

        var exit = app.Run(StartupOptions.Parse([]));

        Assert.Equal(StudioConfigApp.ExitNoTerminal, exit);
        Assert.Contains("needs an interactive terminal", error.ToString(), StringComparison.Ordinal);
    }
}
