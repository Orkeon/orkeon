using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The one double the campaign needs. It has to answer four different verbs from one instance,
/// and it has to be able to hold a run open — a run in flight is a screen, and a launcher that
/// only ever plays to completion cannot photograph one.
/// </summary>
public sealed class ScriptedOrkeonCliTests
{
    private static ProcessLaunchRequest Ask(params string[] arguments) =>
        new() { FileName = "orkeon", Arguments = arguments };

    [Fact]
    public async Task One_instance_answers_each_verb_with_its_own_script()
    {
        var cli = new ScriptedOrkeonCli()
            .Answer("doctor", 0, "[]")
            .Answer("--version", 0, "orkeon 1.0.0-rc.2")
            .Answer("run", 2, "boom");

        Assert.Equal("[]", await FirstLineAsync(cli, Ask("doctor", "--json")));
        Assert.Equal("orkeon 1.0.0-rc.2", await FirstLineAsync(cli, Ask("--version")));

        var failed = await cli.RunAsync(Ask("run", "crew.yaml"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, failed.ExitCode);
    }

    [Fact]
    public async Task An_unscripted_verb_succeeds_silently_rather_than_throwing()
    {
        var cli = new ScriptedOrkeonCli();

        var result = await cli.RunAsync(Ask("forge"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(cli.Requests);
    }

    /// <summary>
    /// A held run stays pending, and lines can still be pushed into it — which is what makes a
    /// screen mid-flight photographable, and then the screen that follows it.
    /// </summary>
    [Fact]
    public async Task A_held_run_stays_open_until_it_is_released()
    {
        var cli = new ScriptedOrkeonCli().Answer("run", 0, "{\"kind\":\"run.started\"}");
        cli.Hold("run");

        var lines = new List<string>();
        var run = cli.RunAsync(
            Ask("run", "crew.yaml"),
            line => lines.Add(line.Text),
            TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted);
        Assert.True(cli.IsParked);

        cli.Emit("{\"kind\":\"task.completed\"}");
        cli.Release();

        var result = await run;
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, lines.Count);
        Assert.Contains("task.completed", lines[1], StringComparison.Ordinal);
    }

    /// <summary>Outside a run there is no listener, and a line falls on the floor — as a dead child would.</summary>
    [Fact]
    public void An_emit_outside_a_run_is_dropped()
    {
        var cli = new ScriptedOrkeonCli();

        cli.Emit("{\"kind\":\"run.started\"}");

        Assert.Empty(cli.Requests);
        Assert.False(cli.IsParked);
    }

    private static async Task<string?> FirstLineAsync(ScriptedOrkeonCli cli, ProcessLaunchRequest request)
    {
        string? first = null;
        await cli.RunAsync(request, line => first ??= line.Text, TestContext.Current.CancellationToken);
        return first;
    }
}
