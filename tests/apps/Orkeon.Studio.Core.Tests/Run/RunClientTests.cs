using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>
/// The watched-run client over a scripted launcher (BUS-06): the argv it composes, the
/// protocol/raw routing of stdout, and what it can say back down stdin — including the hub
/// verbs BUS-05 opened. No real binary anywhere.
/// </summary>
public class RunClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    private static readonly RunTarget Target = new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = "/ws/crew.yaml",
        RunPath = "/ws/crew.yaml",
    };

    private static (RunClient Client, FakeProcessLauncher Processes) Build(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);

        var processes = new FakeProcessLauncher();
        return (new RunClient(processes, new OrkeonBinaryLocator(executables)), processes);
    }

    [Fact]
    public void The_observation_flags_close_the_argv()
    {
        // They come last so the command a user reads still opens with what they chose.
        Assert.Equal(
            ["run", "/ws/crew.yaml", "--events", "jsonl", "--stream", "--client", "studio"],
            RunArgumentsBuilder.Build(Target, new RunLaunchOptions
            {
                Events = true,
                Stream = true,
                ClientName = "studio",
            }));

        // Without --events, nothing is added: the raw-terminal path is unchanged.
        Assert.Equal(
            ["run", "/ws/crew.yaml"],
            RunArgumentsBuilder.Build(Target, new RunLaunchOptions { Stream = true, ClientName = "studio" }));
    }

    [Fact]
    public async Task A_watched_run_always_asks_for_the_protocol()
    {
        // A screen cannot show progress it never asked for, so the client forces the flag
        // rather than silently handing back a terminal.
        var (client, processes) = Build();

        await client.RunAsync(
            Target,
            new RunLaunchOptions { Events = false },
            _ => { },
            workingDirectory: "/ws",
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(processes.Requests);
        Assert.Contains("--events", request.Arguments);
        Assert.Equal("/ws", request.WorkingDirectory);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.NotNull(request.OnInputReady);
    }

    [Fact]
    public async Task Protocol_lines_become_events_and_everything_else_stays_visible_raw()
    {
        var (client, processes) = Build();
        processes
            .WithStandardOutput(
                """{"v":2,"seq":1,"ts":"t","kind":"run.started","target":"/ws/crew.yaml","stream":false}""",
                "a stray non-protocol line")
            .WithStandardError("warning: something");

        var events = new List<OrkeonEvent>();
        var raw = new List<ProcessOutputLine>();

        var result = await client.RunAsync(
            Target, new RunLaunchOptions(), events.Add, raw.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("run.started", Assert.Single(events).Kind);

        // What the stream said stays visible, even on a screen that shows progress.
        Assert.Equal(2, raw.Count);
    }

    [Fact]
    public async Task The_screen_answers_questions_and_speaks_to_the_hub()
    {
        var (client, processes) = Build();
        processes.RunsUntilCancelled = true;

        var run = client.RunAsync(
            Target, new RunLaunchOptions(), _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(client.IsRunning);
        Assert.True(client.Answer("c-1", "yes"));
        Assert.True(client.PostTo("agent://crew/analyst", new { note = "hi" }));
        Assert.True(client.Publish("orders", new { id = 42 }));
        Assert.True(client.Subscribe("progress"));
        Assert.True(client.Unsubscribe("progress"));
        Assert.True(client.ReplyToAgent("c-2", new { answer = "ok" }));
        Assert.True(client.RequestCancellation());
        await run;

        Assert.Equal(
            ["""{"kind":"input.given","correlationId":"c-1","value":"yes"}""",
             """{"kind":"post","to":"agent://crew/analyst","payload":{"note":"hi"}}""",
             """{"kind":"publish","topic":"orders","payload":{"id":42}}""",
             """{"kind":"subscribe","topic":"progress"}""",
             """{"kind":"unsubscribe","topic":"progress"}""",
             """{"kind":"reply","correlationId":"c-2","payload":{"answer":"ok"}}"""],
            processes.InputLines);

        // No child left: the client says so rather than pretending it was delivered.
        Assert.False(client.IsRunning);
        Assert.False(client.Answer("c-1", "too late"));
        Assert.False(client.RequestCancellation());
    }

    [Fact]
    public async Task A_missing_binary_comes_back_as_not_started_and_a_second_run_is_refused()
    {
        var (absent, _) = Build(installed: false);
        var result = await absent.RunAsync(
            Target, new RunLaunchOptions(), _ => { },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.NotStarted, result.Outcome);

        var (client, processes) = Build();
        processes.RunsUntilCancelled = true;
        var run = client.RunAsync(
            Target, new RunLaunchOptions(), _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RunAsync(Target, new RunLaunchOptions(), _ => { },
                cancellationToken: TestContext.Current.CancellationToken));

        client.RequestCancellation();
        await run;
    }
}
