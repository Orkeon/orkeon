namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The console <c>Program.Main</c> hands on is the one every verb writes to: a verb dispatched
/// with a given console writes there and nowhere else, its refusals included.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ProgramConsoleTests
{
    [Fact]
    public async Task ShouldWriteToTheGivenConsoleOnly_WhenAVerbIsDispatched()
    {
        using var console = new TestConsole();
        using var process = new ProcessLogCapture();

        var exit = await Program.DispatchAsync(console, ["--version"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Equal(CliUsage.VersionLine, console.Stdout.TrimEnd());
        Assert.Empty(console.Stderr);
        Assert.Empty(process.Stdout);
        Assert.Empty(process.Stderr);
    }

    [Fact]
    public async Task ShouldRefuseOnTheGivenErrorOutput_WhenAVerbIsUnknown()
    {
        using var console = new TestConsole();
        using var process = new ProcessLogCapture();

        var exit = await Program.DispatchAsync(console, ["no-such-verb"]);

        Assert.NotEqual(Program.ExitOk, exit);
        Assert.Contains("no-such-verb", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(process.Stdout);
        Assert.Empty(process.Stderr);
    }
}
