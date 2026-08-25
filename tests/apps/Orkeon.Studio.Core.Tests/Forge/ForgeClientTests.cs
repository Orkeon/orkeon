using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The forge child-process client over a scripted launcher: the argv it composes, the
/// protocol/raw routing of stdout, and the stdin answers — no real binary anywhere.
/// </summary>
public class ForgeClientTests
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    private static (ForgeClient Client, FakeProcessLauncher Processes) Build(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);
        var processes = new FakeProcessLauncher();
        return (new ForgeClient(processes, new OrkeonBinaryLocator(executables)), processes);
    }

    [Fact]
    public void The_argv_follows_the_cli_grammar()
    {
        Assert.Equal(
            ["forge", "je veux une veille", "--events", "jsonl"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest { Need = "je veux une veille" }));

        Assert.Equal(
            ["forge", "resume", "veille", "--events", "jsonl", "--settings", "/ws/appsettings.json", "--auto"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest
            {
                Need = "ignored when resuming",
                ResumeSlug = "veille",
                SettingsPath = "/ws/appsettings.json",
                Auto = true,
            }));
    }

    [Fact]
    public async Task Protocol_lines_become_events_and_everything_else_stays_visible_raw()
    {
        var (client, processes) = Build();
        processes
            .WithStandardOutput(
                """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}""",
                "some stray non-protocol line")
            .WithStandardError("warning: something");

        var events = new List<OrkeonEvent>();
        var raw = new List<ProcessOutputLine>();
        var result = await client.RunAsync(
            new ForgeStartRequest { Need = "veille", WorkingDirectory = "/ws" },
            events.Add,
            raw.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("session.started", Assert.Single(events).Kind);
        Assert.Equal(2, raw.Count);   // the stray stdout line and the stderr line, both kept

        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal("/ws", request.WorkingDirectory);
        Assert.Equal(["forge", "veille", "--events", "jsonl"], request.Arguments);
        Assert.NotNull(request.OnInputReady);   // the stdin channel is always wired
    }

    [Fact]
    public async Task Messages_and_decisions_go_down_stdin_as_protocol_lines()
    {
        var (client, processes) = Build();
        processes.RunsUntilCancelled = true;

        var run = client.RunAsync(
            new ForgeStartRequest { Need = "veille" },
            _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(client.IsRunning);
        Assert.True(client.SendMessage("exemple.fr, chaque matin"));
        Assert.True(client.SendDecision("refine"));
        // The edit path: the blueprint travels as a JSON object, never as a string —
        // and what could never be a document at all is refused before the wire.
        Assert.True(client.SendBlueprint("""{"crew":{"name":"veille"}}"""));
        Assert.False(client.SendBlueprint("pas du json"));
        Assert.False(client.SendBlueprint("""["un tableau"]"""));
        Assert.True(client.RequestCancellation());
        await run;

        Assert.Equal(
            ["""{"kind":"user.message","text":"exemple.fr, chaque matin"}""",
             """{"kind":"decision.made","value":"refine"}""",
             """{"kind":"blueprint.edited","blueprint":{"crew":{"name":"veille"}}}"""],
            processes.InputLines);
        Assert.False(client.IsRunning);
        Assert.False(client.SendMessage("trop tard"));   // no child: said, not thrown
        Assert.False(client.RequestCancellation());
    }

    [Fact]
    public async Task The_dry_pause_edit_rides_the_launch_itself()
    {
        var (client, processes) = Build();

        // The amended blueprint: `--edit` on the argv, and the blueprint.edited line
        // queued on stdin at launch — before any event can come back, no race.
        await client.RunAsync(
            new ForgeStartRequest
            {
                ResumeSlug = "veille",
                Dry = true,
                EditedBlueprintJson = """{"crew":{"name":"veille"}}""",
            },
            _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(processes.Requests);
        Assert.Equal(["forge", "resume", "veille", "--events", "jsonl", "--dry", "--edit"], request.Arguments);
        Assert.Equal(
            """{"kind":"blueprint.edited","blueprint":{"crew":{"name":"veille"}}}""",
            Assert.Single(processes.InputLines));

        // What could never be a document at all is refused before any child starts.
        var refused = await client.RunAsync(
            new ForgeStartRequest { ResumeSlug = "veille", EditedBlueprintJson = "pas du json" },
            _ => { },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.NotStarted, refused.Outcome);
        Assert.Single(processes.Requests);   // still just the first launch
    }

    [Fact]
    public async Task A_missing_binary_comes_back_as_not_started_and_a_second_run_is_refused_while_one_lives()
    {
        var (absent, _) = Build(installed: false);
        var result = await absent.RunAsync(
            new ForgeStartRequest { Need = "x" }, _ => { }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.NotStarted, result.Outcome);

        var (client, processes) = Build();
        processes.RunsUntilCancelled = true;
        var run = client.RunAsync(
            new ForgeStartRequest { Need = "x" }, _ => { }, cancellationToken: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RunAsync(new ForgeStartRequest { Need = "y" }, _ => { }, cancellationToken: TestContext.Current.CancellationToken));
        client.RequestCancellation();
        await run;
    }
}

/// <summary>
/// The resume seeding: the stream never replays the past, the artifacts carry it. Each
/// artifact is wrapped into the synthetic event the live stream would have emitted — one
/// reading of every shape, and the same tolerance as everywhere else.
/// </summary>
public sealed class ForgeSessionHydratorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "orkeon-studio-forge-hydrate-" + Guid.NewGuid().ToString("N"));

    public ForgeSessionHydratorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_session_directory_seeds_the_whole_projection()
    {
        File.WriteAllText(Path.Combine(_directory, "transcript.jsonl"),
            """{"ts":"t","role":"user","text":"je veux une veille"}""" + "\n"
            + """{"ts":"t","role":"assistant","text":"Quel fournisseur ?"}""" + "\n"
            + "{ truncated tail");
        File.WriteAllText(Path.Combine(_directory, "brief.json"),
            """{"goal":"Veille fournisseurs","acceptance":[{"id":"A1","statement":"Cite ses sources","kind":"must"}]}""");
        File.WriteAllText(Path.Combine(_directory, "blueprint.json"),
            """{"crew":{"name":"veille","goal":"Veiller"},"agents":[{"key":"c","role":"Web Researcher"}],"tasks":[{"key":"t1","description":"Collecter","agent":"c"}],"rationale":"Un seul rôle."}""");
        File.WriteAllText(Path.Combine(_directory, "verdict.json"),
            """{"score":0.85,"passing":true,"findings":[],"suggestions":[],"judge":"llm"}""");

        var model = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(model, _directory);

        Assert.Equal(["user", "assistant"], model.Messages.Select(m => m.Role));
        // The hydrated blueprint's crew name supersedes the brief goal, like the live stream.
        Assert.Equal("veille", model.Title);
        Assert.Equal("Cite ses sources", Assert.Single(model.Criteria).Statement);
        Assert.Equal("Collecter", Assert.Single(model.Proposal!.Steps).Description);
        Assert.Equal("Web Researcher", model.Proposal.Steps[0].AgentRole);
        Assert.True(model.Verdict!.Passing);
        Assert.Equal("llm", model.Verdict.Judge);
    }

    [Fact]
    public void An_empty_or_broken_directory_seeds_nothing_and_blocks_nothing()
    {
        File.WriteAllText(Path.Combine(_directory, "brief.json"), "{ not json");

        var model = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(model, _directory);
        ForgeSessionHydrator.Hydrate(model, Path.Combine(_directory, "ghost"));

        Assert.Empty(model.Messages);
        Assert.Empty(model.Criteria);
        Assert.Null(model.Proposal);
    }
}

/// <summary>
/// "Mes solutions" read straight off the disk: the CLI's session layout, re-declared and
/// pinned by a verbatim <c>session.json</c> fixture — tolerance and ordering included.
/// </summary>
public sealed class ForgeSessionCatalogTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-studio-forge-catalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private void WriteSession(string slug, string json)
    {
        var directory = Path.Combine(_workspace, ".orkeon", "forge", slug);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "session.json"), json);
    }

    [Fact]
    public void Sessions_list_most_recent_first_with_their_actions()
    {
        // Verbatim shape of the CLI's session.json (v1) — the drift pin.
        WriteSession("veille", """
            {"v":2,"slug":"veille","title":"Veille fournisseurs","format":"yaml","state":"Promoted","status":"Promoted",
             "iteration":2,"repairAttempts":0,"budget":{"maxIterations":3},"promotedTo":"/solutions/veille",
             "createdAt":"2026-08-18T09:00:00Z","updatedAt":"2026-08-19T08:00:00Z"}
            """);
        WriteSession("rapport", """
            {"v":2,"slug":"rapport","title":"Rapport hebdo","format":"yaml","state":"Test","status":"Active",
             "iteration":1,"repairAttempts":0,"budget":{},"createdAt":"2026-08-19T10:00:00Z","updatedAt":"2026-08-19T10:30:00Z"}
            """);
        WriteSession("cassee", "{ not json at all");

        var solutions = ForgeSessionCatalog.List(_workspace);

        // The corrupt one is skipped, never a crash; freshest first.
        Assert.Equal(["rapport", "veille"], solutions.Select(s => s.Slug));

        var inProgress = solutions[0];
        Assert.True(inProgress.CanResume);
        Assert.False(inProgress.CanRelaunch);

        var adopted = solutions[1];
        Assert.Equal("Veille fournisseurs", adopted.Title);
        Assert.True(adopted.CanRelaunch);
        Assert.False(adopted.CanResume);
        Assert.Equal("/solutions/veille", adopted.PromotedTo);
    }

    [Fact]
    public void A_workspace_without_sessions_is_an_empty_list()
    {
        Directory.CreateDirectory(_workspace);
        Assert.Empty(ForgeSessionCatalog.List(_workspace));
    }

    [Fact]
    public void The_reverse_lookup_finds_the_session_that_adopted_a_team()
    {
        // W-09 «Modifier»: the sidecar records no session; promotedTo is the only link.
        var teamDirectory = Path.Combine(_workspace, "teams", "veille-docs");
        Directory.CreateDirectory(teamDirectory);
        WriteSession("veille", $$"""
            {"v":2,"slug":"veille","title":"Veille","format":"yaml","state":"Promoted","status":"Promoted",
             "promotedTo":{{System.Text.Json.JsonSerializer.Serialize(teamDirectory + Path.DirectorySeparatorChar)}},
             "updatedAt":"2026-08-19T08:00:00Z"}
            """);
        WriteSession("autre", """
            {"v":2,"slug":"autre","format":"yaml","state":"Test","status":"Active","updatedAt":"2026-08-19T09:00:00Z"}
            """);

        // Trailing separators do not defeat the match; an unadopted folder finds nothing.
        Assert.Equal("veille", ForgeSessionCatalog.FindByPromotedTo(_workspace, teamDirectory)?.Slug);
        Assert.Null(ForgeSessionCatalog.FindByPromotedTo(_workspace, Path.Combine(_workspace, "teams", "inconnue")));
    }
}
