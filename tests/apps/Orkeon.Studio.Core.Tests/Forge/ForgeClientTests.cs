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

        // Adoption without a trial: the same resume, told to stop at Ready instead of
        // running the crew. The engine refuses it anywhere but the dry pause.
        Assert.Equal(
            ["forge", "resume", "veille", "--events", "jsonl", "--adopt"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest { ResumeSlug = "veille", Adopt = true }));
    }

    /// <summary>
    /// STUDIO-14 D-09: the trial reads the folder the wizard's first step bound, through the
    /// engine's <c>--read</c>. Emitted only when a folder is known — the golden argv above
    /// carries no <c>--read</c>, so an older engine never meets the option for a team that
    /// reads nothing.
    /// </summary>
    [Fact]
    public void The_argv_carries_the_read_root_when_one_is_known()
    {
        Assert.Equal(
            ["forge", "resume", "veille", "--events", "jsonl", "--read", "/data/notes"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest { ResumeSlug = "veille", ReadDirectory = "/data/notes" }));

        Assert.Equal(
            ["forge", "je veux une veille", "--events", "jsonl", "--read", "/data/notes", "--dry"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest
            {
                Need = "je veux une veille",
                ReadDirectory = "/data/notes",
                Dry = true,
            }));

        // Blank is "unknown", not an empty folder name the engine would refuse.
        Assert.DoesNotContain(
            "--read",
            ForgeArgumentsBuilder.Build(new ForgeStartRequest { ResumeSlug = "veille", ReadDirectory = "  " }));
    }

    /// <summary>
    /// STUDIO-26, D-01: the promotion carries the team's name — the title the engine gives the
    /// card, the record and the session — as one argument after <c>--name</c>, a leading dash
    /// included: the engine takes that value as written. No name, no option.
    /// </summary>
    [Fact]
    public async Task The_promote_argv_carries_the_team_name()
    {
        Assert.Equal(
            ["forge", "promote", "veille", "--to", "/teams/ma-veille", "--name", "Ma veille", "--events", "jsonl",
             "--schedule", "daily@07:30"],
            ForgeArgumentsBuilder.BuildPromote("veille", "/teams/ma-veille", "Ma veille", "daily@07:30"));
        Assert.Equal(
            ["forge", "promote", "veille", "--to", "/teams/veille", "--name", "-Veille-", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildPromote("veille", "/teams/veille", "-Veille-", schedule: null));
        Assert.Equal(
            ["forge", "promote", "veille", "--to", "/teams/veille", "--events", "jsonl"],
            ForgeArgumentsBuilder.BuildPromote("veille", "/teams/veille", teamName: "  ", schedule: null));

        var (client, processes) = Build();
        await client.PromoteAsync(
            "veille", "/teams/ma-veille", "Ma veille", schedule: null, "/ws", _ => { },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            ["forge", "promote", "veille", "--to", "/teams/ma-veille", "--name", "Ma veille", "--events", "jsonl"],
            Assert.Single(processes.Requests).Arguments);
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

    /// <summary>
    /// The identity comes from <c>session.json</c> as the live <c>session.started</c> would carry
    /// it — slug, directory, format and, since STUDIO-25, the session's stable id.
    /// </summary>
    [Fact]
    public void The_identity_is_read_from_the_session_file_id_included()
    {
        File.WriteAllText(Path.Combine(_directory, "session.json"),
            """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille","format":"yaml","state":"Test","status":"Active"}""");

        var model = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(model, _directory);

        Assert.Equal("veille", model.Slug);
        Assert.Equal(_directory, model.Directory);
        Assert.Equal("yaml", model.Format);
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), model.SessionId);
    }

    /// <summary>
    /// The recalled verdict carries the whole cost of the last trial, not a subset of it.
    /// The live <c>verdict.ready</c> declares six figures (W-08) and <c>last-run.json</c>
    /// persists all six, so a resume that folds in only some of them shows a screen the
    /// live one would not have shown — silently, since a missing figure is a missing chip
    /// rather than an error.
    /// </summary>
    [Fact]
    public void The_recalled_verdict_carries_every_metric_the_last_run_persisted()
    {
        File.WriteAllText(Path.Combine(_directory, "last-run.json"),
            """{"run":1,"success":true,"durationMs":59000,"tokens":12840,"promptTokens":9600,"completionTokens":3240,"cacheHitTokens":7980,"cacheMissTokens":1620}""");
        File.WriteAllText(Path.Combine(_directory, "verdict.json"),
            """{"score":0.78,"passing":true,"findings":[],"suggestions":[],"judge":"llm"}""");

        var model = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(model, _directory);

        Assert.Equal(59000L, model.Verdict!.DurationMs);
        Assert.Equal(12840L, model.Verdict.Tokens);
        Assert.Equal(9600L, model.Verdict.PromptTokens);
        Assert.Equal(3240L, model.Verdict.CompletionTokens);
        Assert.Equal(7980L, model.Verdict.CacheHitTokens);
        Assert.Equal(1620L, model.Verdict.CacheMissTokens);
    }

    /// <summary>
    /// The verdict's own figure wins: <c>verdict.json</c> is the arbitration that was
    /// actually reached, and <c>last-run.json</c> only fills what it left unsaid.
    /// </summary>
    [Fact]
    public void The_verdicts_own_metric_is_not_overwritten_by_the_last_run()
    {
        File.WriteAllText(Path.Combine(_directory, "last-run.json"),
            """{"tokens":12840,"promptTokens":9600}""");
        File.WriteAllText(Path.Combine(_directory, "verdict.json"),
            """{"score":0.78,"passing":true,"judge":"llm","promptTokens":42}""");

        var model = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(model, _directory);

        Assert.Equal(42L, model.Verdict!.PromptTokens);
        Assert.Equal(12840L, model.Verdict.Tokens);
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
            {"v":2,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille","title":"Veille fournisseurs","format":"yaml","state":"Promoted","status":"Promoted",
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
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), adopted.Id);
        Assert.Null(inProgress.Id);
    }

    [Fact]
    public void A_workspace_without_sessions_is_an_empty_list()
    {
        Directory.CreateDirectory(_workspace);
        Assert.Empty(ForgeSessionCatalog.List(_workspace));
    }

    /// <summary>
    /// STUDIO-25: a session is found by the id its <c>session.json</c> carries — the id a team's
    /// <c>forge.json</c> names — never by comparing paths. A session written before the id
    /// carries none and is found by nothing (D-06); two sessions sharing an id (a session
    /// directory copied by hand) answer with the most recently touched, the CLI's pick too.
    /// </summary>
    [Fact]
    public void A_session_is_found_by_its_id_and_never_by_a_path()
    {
        var id = Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f");
        WriteSession("veille", """
            {"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille","format":"yaml","state":"Promoted",
             "status":"Promoted","promotedTo":"/teams/veille","updatedAt":"2026-08-19T08:00:00Z"}
            """);
        WriteSession("veille-copie-a-la-main", """
            {"v":1,"id":"6F1C2A0E-4B7D-4E9A-9F53-1D2C3B4A5E6F","slug":"veille-copie-a-la-main","format":"yaml","state":"Promoted",
             "status":"Promoted","promotedTo":"/teams/veille","updatedAt":"2026-08-19T09:00:00Z"}
            """);
        WriteSession("ancienne", """
            {"v":1,"slug":"ancienne","format":"yaml","state":"Promoted","status":"Promoted",
             "promotedTo":"/teams/ancienne","updatedAt":"2026-08-19T10:00:00Z"}
            """);
        WriteSession("illisible", """
            {"v":1,"id":"pas-un-id","slug":"illisible","format":"yaml","state":"Test","status":"Active","updatedAt":"2026-08-19T11:00:00Z"}
            """);

        Assert.Equal("veille-copie-a-la-main", ForgeSessionCatalog.FindById(_workspace, id)?.Slug);
        Assert.Null(ForgeSessionCatalog.FindById(_workspace, Guid.NewGuid()));
        Assert.Null(ForgeSessionCatalog.FindById(_workspace, Guid.Empty));
        var listed = ForgeSessionCatalog.List(_workspace);
        Assert.Null(listed.Single(s => s.Slug == "ancienne").Id);
        Assert.Null(listed.Single(s => s.Slug == "illisible").Id);
    }

    /// <summary>
    /// STUDIO-25: Studio reads the id a team folder's <c>forge.json</c> carries — read-only, the
    /// CLI writes it. Absent, broken, or not an id: no id, never a throw.
    /// </summary>
    [Fact]
    public void The_id_a_team_folder_carries_is_read_from_its_forge_json()
    {
        var team = Path.Combine(_workspace, "teams", "veille");
        Directory.CreateDirectory(team);
        var record = Path.Combine(team, ForgeSessionCatalog.TeamRecordFileName);

        Assert.Null(ForgeSessionCatalog.ReadTeamSessionId(team));

        // Verbatim shape of the CLI's forge.json — the drift pin.
        File.WriteAllText(record, """
            {"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","slug":"veille","title":"Veille","format":"yaml",
             "promotedAt":"2026-08-19T08:00:00Z","brief":{"goal":"Veille fournisseurs"}}
            """);
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), ForgeSessionCatalog.ReadTeamSessionId(team));

        File.WriteAllText(record, """{"v":1,"id":"pas-un-id","slug":"veille"}""");
        Assert.Null(ForgeSessionCatalog.ReadTeamSessionId(team));
        File.WriteAllText(record, """{"v":1,"slug":"veille"}""");
        Assert.Null(ForgeSessionCatalog.ReadTeamSessionId(team));
        File.WriteAllText(record, "{ not json");
        Assert.Null(ForgeSessionCatalog.ReadTeamSessionId(team));
        Assert.Null(ForgeSessionCatalog.ReadTeamSessionId(Path.Combine(_workspace, "nulle-part")));
    }

    /// <summary>
    /// FORGE-09: « Modify » on a team no session points at runs <c>forge reopen</c> on the
    /// folder — and nothing else: the verb starts no cycle, and the engine refuses every
    /// cycle option on it, so the request's other fields never reach the argv.
    /// </summary>
    [Fact]
    public void The_reopen_argv_carries_the_team_folder_and_nothing_else()
    {
        Assert.Equal(
            ["forge", "reopen", "/teams/veille", "--events", "jsonl"],
            ForgeArgumentsBuilder.Build(new ForgeStartRequest
            {
                ReopenDirectory = "/teams/veille",
                ResumeSlug = "ignored",
                SettingsPath = "/ws/appsettings.json",
                ReadDirectory = "/data",
                Dry = true,
            }));
    }
}
