using System.Reflection;
using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>The <c>--schedule</c> grammar: two spellings, everything else refused with the remedy.</summary>
public sealed class ForgeScheduleTests
{
    [Fact]
    public void Daily_parses_its_time()
    {
        Assert.True(ForgeSchedule.TryParse("daily@07:30", out var schedule, out _));
        Assert.Equal(ForgeSchedule.KindDaily, schedule!.Kind);
        Assert.Equal(7, schedule.Hour);
        Assert.Equal(30, schedule.Minute);
    }

    [Fact]
    public void Hourly_parses_case_insensitively()
    {
        Assert.True(ForgeSchedule.TryParse("Hourly", out var schedule, out _));
        Assert.Equal(ForgeSchedule.KindHourly, schedule!.Kind);
    }

    [Theory]
    [InlineData("weekly")]
    [InlineData("daily@25:00")]
    [InlineData("daily@0730")]
    public void Anything_else_is_refused_with_the_remedy(string text)
    {
        Assert.False(ForgeSchedule.TryParse(text, out _, out var error));
        Assert.Contains("daily@HH:mm", error, StringComparison.Ordinal);
    }
}

/// <summary>
/// The promotion (SPEC-ORKEON-FORGE §11): a Ready session leaves its directory as an
/// ordinary folder — crew copy, launch scripts against the CLI's own run grammar,
/// FORGE.md identity card, generated schedule artifacts with the install command
/// displayed and never executed.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgePromoteTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-promote-" + Guid.NewGuid().ToString("N"));

    /// <summary>A team folder the way Studio names one: under the teams root, after the team's name.</summary>
    private string Destination => Path.Combine(_workspace, "teams", "ma-veille");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ForgeSession ReadySession(string slug = "veille", bool withVerdict = true)
    {
        var session = ForgeSession.Create(_workspace, slug);
        session.Document.Title = "Résumer chaque matin les nouvelles offres";

        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);
        if (withVerdict)
        {
            session.SaveArtifact("verdict.json", new ForgeVerdict
            {
                Score = 0.85,
                Passing = true,
                Judge = ForgeVerdict.JudgeLlm,
                Findings = [new ForgeFinding { Id = "F1", Severity = "minor", Statement = "Un prix manque", Acceptance = "A2" }],
            });
        }

        var crew = Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName);
        Directory.CreateDirectory(Path.Combine(crew, "agents"));
        File.WriteAllText(Path.Combine(crew, "config.yaml"), "name: veille\n");
        File.WriteAllText(Path.Combine(crew, "agents", "collecteur.yaml"), "role: Collecteur\n");

        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(Now);
        return session;
    }

    /// <summary>
    /// A reading tool and a deliverable landing on the SAME virtual root yield ONE
    /// <c>--mount</c>, and it is writable.
    /// <para>
    /// The read mount is derived first, so a deliverable under <c>/workspace</c> met a
    /// read-only entry and was skipped on the name alone: the promoted team launched with
    /// <c>/workspace:ro</c> and could never write the deliverable it was built to produce —
    /// the resolver logged a warning and the run reported that it had finished. Studio's
    /// sibling derivation deduped the other way and showed two chips for one root. Both hold
    /// the same rule now.
    /// </para>
    /// </summary>
    [Fact]
    public void A_reading_tool_and_a_deliverable_on_one_root_yield_one_writable_mount()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, new ForgeBlueprint
        {
            Crew = new ForgeBlueprintCrew { Name = "veille", Goal = "g" },
            Agents = [new ForgeBlueprintAgent { Key = "a", Role = "A", Goal = "G", Tools = ["file_read", "file_write"] }],
            Tasks = [new ForgeBlueprintTask
            {
                Key = "t", Description = "d", ExpectedOutput = "e", Agent = "a",
                Deliverable = "/workspace/rapport.md",
            }],
        });

        var result = ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var launcher = File.ReadAllText(Path.Combine(result.Destination, result.Launcher));
        var workspaceMounts = launcher
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Contains(":/workspace:", StringComparison.Ordinal))
            .ToList();

        Assert.Single(workspaceMounts);
        Assert.EndsWith(":/workspace:rw\"", workspaceMounts[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_promoted_folder_is_complete_and_its_scripts_carry_the_sample_inputs()
    {
        var session = ReadySession();

        var result = ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        // The crew travelled as-is.
        Assert.True(File.Exists(Path.Combine(Destination, "crew", "agents", "collecteur.yaml")));

        // Both launchers exist; the POSIX one expands its own directory and carries the
        // brief's sample inputs, flagged as the thing to adapt.
        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Contains("exec orkeon", posix, StringComparison.Ordinal);
        Assert.Contains("run \"$DIR/crew\"", posix, StringComparison.Ordinal);
        Assert.Contains("--var 'supplier_url=https://exemple.fr/offres'", posix, StringComparison.Ordinal);
        Assert.Contains("--initial-context 'Premier essai'", posix, StringComparison.Ordinal);
        Assert.DoesNotContain("--settings", posix, StringComparison.Ordinal);
        Assert.Contains("adapt them", posix, StringComparison.Ordinal);

        var windows = File.ReadAllText(Path.Combine(Destination, ForgePromoter.WindowsLauncherName));
        Assert.Contains("run \"%~dp0crew\"", windows, StringComparison.Ordinal);
        Assert.Contains("--var \"supplier_url=https://exemple.fr/offres\"", windows, StringComparison.Ordinal);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), "run.sh must be executable");
        }

        // The identity card speaks the brief's language and quotes what was promised and judged.
        var card = File.ReadAllText(Path.Combine(Destination, ForgePromoter.CardFileName));
        Assert.Contains("# Résumer chaque matin les nouvelles offres", card, StringComparison.Ordinal);
        Assert.Contains("Critères d'acceptation", card, StringComparison.Ordinal);
        Assert.Contains("**A1** (must) — Le résumé cite ses sources", card, StringComparison.Ordinal);
        Assert.Contains("Score 0.85 — conforme (juge: llm)", card, StringComparison.Ordinal);
        Assert.Contains("[minor] Un prix manque (A2)", card, StringComparison.Ordinal);
        // Read the version from the same assembly the card stamps it from: pinning the
        // literal would make every release bump a test failure.
        var version = typeof(ForgePromoter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.Contains($"Orkeon {version}", card, StringComparison.Ordinal);

        Assert.Equal(ForgePromoter.PosixLauncherName, result.Launcher);
        Assert.Null(result.ScheduleDirectory);
        Assert.Null(result.InstallCommand);
    }

    [Fact]
    public void A_daily_schedule_generates_the_three_families_and_the_platform_install_command()
    {
        var session = ReadySession();
        Assert.True(ForgeSchedule.TryParse("daily@07:30", out var schedule, out _));

        var result = ForgePromoter.Promote(
            session, Destination, schedule, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var scheduleDir = Path.Combine(Destination, ForgePromoter.ScheduleDirectoryName);
        Assert.Contains("30 7 * * *", File.ReadAllText(Path.Combine(scheduleDir, "cron.txt")), StringComparison.Ordinal);
        Assert.Contains("OnCalendar=*-*-* 07:30:00",
            File.ReadAllText(Path.Combine(scheduleDir, "orkeon-ma-veille.timer")), StringComparison.Ordinal);
        Assert.Contains("run.sh", File.ReadAllText(Path.Combine(scheduleDir, "orkeon-ma-veille.service")), StringComparison.Ordinal);

        // 10:00 > 07:30 on the fixed clock: the Windows task anchors on tomorrow's occurrence.
        var task = File.ReadAllText(Path.Combine(scheduleDir, "windows-task.xml"));
        Assert.Contains("<StartBoundary>2026-08-20T07:30:00</StartBoundary>", task, StringComparison.Ordinal);
        Assert.Contains("run.cmd", task, StringComparison.Ordinal);

        Assert.Equal(ForgePromoter.ScheduleDirectoryName, result.ScheduleDirectory);
        Assert.Contains("systemctl --user enable --now orkeon-ma-veille.timer", result.InstallCommand, StringComparison.Ordinal);

        // The card shows the install command; nothing was executed.
        var card = File.ReadAllText(Path.Combine(Destination, ForgePromoter.CardFileName));
        Assert.Contains("Planification", card, StringComparison.Ordinal);
        Assert.Contains(result.InstallCommand!, card, StringComparison.Ordinal);
    }

    [Fact]
    public void An_hourly_schedule_speaks_every_dialect()
    {
        var session = ReadySession();
        Assert.True(ForgeSchedule.TryParse("hourly", out var schedule, out _));

        var result = ForgePromoter.Promote(
            session, Destination, schedule, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Windows, Now);

        var scheduleDir = Path.Combine(Destination, ForgePromoter.ScheduleDirectoryName);
        Assert.Contains("0 * * * *", File.ReadAllText(Path.Combine(scheduleDir, "cron.txt")), StringComparison.Ordinal);
        Assert.Contains("OnCalendar=hourly",
            File.ReadAllText(Path.Combine(scheduleDir, "orkeon-ma-veille.timer")), StringComparison.Ordinal);
        Assert.Contains("<Interval>PT1H</Interval>",
            File.ReadAllText(Path.Combine(scheduleDir, "windows-task.xml")), StringComparison.Ordinal);

        Assert.Equal(ForgePromoter.WindowsLauncherName, result.Launcher);
        Assert.StartsWith("schtasks /Create", result.InstallCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_crew_promotes_with_the_script_as_run_target()
    {
        var session = ReadySession();
        session.Document.Format = ForgeSession.FormatScript;
        session.Save(Now);
        var crew = Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName);
        Directory.Delete(crew, recursive: true);
        Directory.CreateDirectory(crew);
        File.WriteAllText(Path.Combine(crew, ForgeScriptRenderer.ScriptFileName), "/// <reference orkeon-script=\"1.0\" />\n");

        ForgePromoter.Promote(session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        Assert.True(File.Exists(Path.Combine(Destination, "crew", ForgeScriptRenderer.ScriptFileName)));

        // The launcher targets the script file, and spells none of the flags `orkeon run`
        // documents as ignored on the script path.
        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Contains("run \"$DIR/crew/crew.ork.ts\"", posix, StringComparison.Ordinal);
        Assert.DoesNotContain("--var", posix, StringComparison.Ordinal);
        Assert.DoesNotContain("--initial-context", posix, StringComparison.Ordinal);

        var card = File.ReadAllText(Path.Combine(Destination, ForgePromoter.CardFileName));
        Assert.Contains("orkeon run crew/crew.ork.ts", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_stay_out_of_the_folder_unless_asked_for()
    {
        Directory.CreateDirectory(_workspace);
        var settings = Path.Combine(_workspace, "appsettings.json");
        File.WriteAllText(settings, """{ "Llm": { "ApiKey": "secret" } }""");

        // Without --with-settings: the scripts point at the file in place, no copy.
        var session = ReadySession();
        ForgePromoter.Promote(session, Destination, schedule: null, settings, copySettings: false,
            ForgePromotePlatform.Linux, Now);
        Assert.False(File.Exists(Path.Combine(Destination, ForgePromoter.SettingsFileName)));
        Assert.Contains($"--settings '{settings}'",
            File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName)), StringComparison.Ordinal);

        // With it: the copy travels and the scripts anchor to the folder.
        var second = Path.Combine(_workspace, "promoted-2");
        ForgePromoter.Promote(session, second, schedule: null, settings, copySettings: true,
            ForgePromotePlatform.Linux, Now);
        Assert.Equal("""{ "Llm": { "ApiKey": "secret" } }""",
            File.ReadAllText(Path.Combine(second, ForgePromoter.SettingsFileName)));
        Assert.Contains("--settings \"$DIR/appsettings.json\"",
            File.ReadAllText(Path.Combine(second, ForgePromoter.PosixLauncherName)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_empty_destination_and_a_missing_crew_are_refused_cleanly()
    {
        var session = ReadySession();
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "keep.txt"), "mine");

        var full = Assert.Throws<InvalidOperationException>(() =>
            ForgePromoter.Promote(session, Destination, null, null, false, ForgePromotePlatform.Linux, Now));
        Assert.Contains("not empty", full.Message, StringComparison.Ordinal);

        Directory.Delete(Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName), recursive: true);
        var bare = Assert.Throws<InvalidOperationException>(() =>
            ForgePromoter.Promote(session, Path.Combine(_workspace, "elsewhere"), null, null, false, ForgePromotePlatform.Linux, Now));
        Assert.Contains("no rendered crew", bare.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_adoption_updates_the_same_folder_in_place()
    {
        // W-09: the one exception to the fresh-directory rule is the folder THIS session
        // already promoted to. Generated artifacts are regenerated; the user's own files
        // — the Studio sidecar, outputs — survive; a dropped schedule is removed.
        var session = ReadySession();
        Assert.True(ForgeSchedule.TryParse("daily@07:30", out var daily, out _));
        var first = ForgePromoter.Promote(
            session, Destination, daily, null, false, ForgePromotePlatform.Linux, Now);
        Assert.False(first.Updated);
        Assert.True(Directory.Exists(Path.Combine(Destination, ForgePromoter.ScheduleDirectoryName)));

        session.Document.PromotedTo = Destination;
        File.WriteAllText(Path.Combine(Destination, "studio-team.json"), """{"name":"Veille"}""");
        File.WriteAllText(Path.Combine(Destination, "note.txt"), "mine");

        var second = ForgePromoter.Promote(
            session, Destination, schedule: null, null, false, ForgePromotePlatform.Linux, Now);

        Assert.True(second.Updated);
        Assert.False(Directory.Exists(Path.Combine(Destination, ForgePromoter.ScheduleDirectoryName)));
        Assert.True(File.Exists(Path.Combine(Destination, "crew", "config.yaml")));
        Assert.Equal("""{"name":"Veille"}""", File.ReadAllText(Path.Combine(Destination, "studio-team.json")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Destination, "note.txt")));
    }

    /// <summary>
    /// Rule R, case 3 (STUDIO-25): the promoted folder moved or renamed since is still this
    /// session's folder — its record carries the session's id and nothing is left where
    /// <c>promotedTo</c> says. Re-adoption updates it where it now is.
    /// </summary>
    [Fact]
    public void Re_adoption_follows_the_promoted_folder_where_it_was_moved()
    {
        var session = ReadySession();
        ForgePromoter.Promote(session, Destination, null, null, false, ForgePromotePlatform.Linux, Now);
        session.Document.PromotedTo = Destination;
        var moved = Path.Combine(_workspace, "renamed");
        Directory.Move(Destination, moved);
        File.WriteAllText(Path.Combine(moved, "note.txt"), "mine");

        var again = ForgePromoter.Promote(session, moved, null, null, false, ForgePromotePlatform.Linux, Now);

        Assert.True(again.Updated);
        Assert.True(File.Exists(Path.Combine(moved, "crew", "config.yaml")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(moved, "note.txt")));
    }

    /// <summary>
    /// Rule R, case 2 (STUDIO-25): a copy of the promoted folder carries the same id while its
    /// original is still where the session says. It is not this session's folder: promoting into
    /// it is refused like any non-empty directory, and the refusal says why and what to do.
    /// </summary>
    [Fact]
    public void A_copy_of_the_promoted_folder_is_refused_as_a_destination()
    {
        var session = ReadySession();
        ForgePromoter.Promote(session, Destination, null, null, false, ForgePromotePlatform.Linux, Now);
        session.Document.PromotedTo = Destination;
        var copy = Path.Combine(_workspace, "copie");
        ForgePromoter.CopyDirectory(Destination, copy);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            ForgePromoter.Promote(session, copy, null, null, false, ForgePromotePlatform.Linux, Now));

        Assert.Contains("not empty", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("copy", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("forge reopen", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_foreign_non_empty_destination_stays_refused_even_after_a_promotion()
    {
        var session = ReadySession();
        session.Document.PromotedTo = Path.Combine(_workspace, "elsewhere");

        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "keep.txt"), "mine");

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            ForgePromoter.Promote(session, Destination, null, null, false, ForgePromotePlatform.Linux, Now));
        Assert.Contains("not empty", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_promote_verb_ships_the_session_and_records_the_transition()
    {
        ReadySession();
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(
            ["promote", "veille", "--to", Destination, "--events", "jsonl"], _workspace);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(Destination, ForgePromoter.CardFileName)));

        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "promoted", "session.renamed", "session.finished"], Kinds(events));
        Assert.Equal(Destination, events[1].GetProperty("path").GetString());
        Assert.False(events[1].TryGetProperty("schedule", out _));
        Assert.Equal("ready", events[3].GetProperty("status").GetString());

        // The session moved to Promoted — under its team's name now — with the transition in the
        // history, and refuses a second run.
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "ma-veille", out var promoted, out _));
        Assert.Equal(ForgeState.Promoted, promoted!.State);
        Assert.Equal(ForgeSessionStatus.Promoted, promoted.Status);
        Assert.Equal(Destination, promoted.Document.PromotedTo);
        Assert.Contains("\"trigger\":\"Promote\"",
            await File.ReadAllTextAsync(Path.Combine(promoted.Directory, ForgeSession.HistoryFileName), TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);

        using var again = new TestConsole();
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote", "ma-veille", "--to", Destination], _workspace));
        Assert.Contains("only a ready session", again.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_promotion_reports_its_code_and_leaves_the_session_ready()
    {
        ReadySession();
        Directory.CreateDirectory(Destination);
        await File.WriteAllTextAsync(Path.Combine(Destination, "keep.txt"), "mine", TestContext.Current.CancellationToken);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(
            ["promote", "veille", "--to", Destination, "--name", "Ma veille", "--events", "jsonl"], _workspace);

        Assert.Equal(1, exitCode);
        var error = Events(console.Stdout).Single(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeErrorCodes.PromoteFailed, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("recoverable").GetBoolean());

        // Nothing moved: the name, the folder and the state all wait for a promotion that is written.
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var session, out _));
        Assert.Equal(ForgeSessionStatus.Ready, session!.Status);
        Assert.Equal("Résumer chaque matin les nouvelles offres", session.Document.Title);
        Assert.DoesNotContain(Events(console.Stdout), e => e.GetProperty("kind").GetString() == "session.renamed");
        Assert.False(File.Exists(Path.Combine(Destination, ForgePromoter.CardFileName)));
    }

    [Fact]
    public async Task The_promote_grammar_is_loud_about_what_it_needs()
    {
        using var console = new TestConsole();

        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote"], _workspace));
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote", "veille"], _workspace));
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote", "veille", "--to", Destination, "--schedule", "weekly"], _workspace));
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["--to", Destination], _workspace));
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote", "ghost", "--to", Destination], _workspace));

        Assert.Contains("promote needs a session slug", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("promote needs --to", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("daily@HH:mm", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The promise the trial makes must survive adoption. The bench mounts /output and the
    /// test stage refuses a run that did not write its promised deliverable — but the
    /// promoted launchers carried no --mount at all, so the same team, launched from its own
    /// folder, was denied /output, logged a warning and reported success with nothing
    /// written. The launcher now carries the mounts the blueprint asks for, anchored on the
    /// folder so the team stays movable, and the folders exist before the first launch (an
    /// absent mount base path is fatal at host build).
    /// </summary>
    [Fact]
    public void The_launchers_carry_the_folders_the_blueprint_writes_to()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"crew":{"name":"veille"},
             "agents":[{"key":"collecteur","role":"Collecteur","goal":"Trouver","tools":["file_write"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"collecteur","deliverable":"/output/rapport.md"},
                      {"key":"t2","description":"d","expectedOutput":"e","agent":"collecteur","deliverable":"/output/annexe.md"}]}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        // The folder exists before anything runs.
        Assert.True(Directory.Exists(Path.Combine(Destination, "output")));

        // One --mount flag, one spec per root, deduplicated, anchored on the script's dir.
        // The physical segment carries the mount grammar's own quotes: the anchor expands to
        // a path the generator does not know, and on Windows it always contains a ':'.
        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Contains("--mount \"\\\"$DIR/output\\\":/output:rw\"", posix, StringComparison.Ordinal);
        Assert.Equal(1, posix.Split("--mount").Length - 1);

        var windows = File.ReadAllText(Path.Combine(Destination, ForgePromoter.WindowsLauncherName));
        Assert.Contains("--mount \"\\\"%~dp0output\\\":/output:rw\"", windows, StringComparison.Ordinal);

        // And the card says where the mounts land, so the folder explains itself.
        var card = File.ReadAllText(Path.Combine(Destination, ForgePromoter.CardFileName));
        Assert.Contains("/output", card, StringComparison.Ordinal);
        Assert.Contains("output/", card, StringComparison.Ordinal);
    }

    /// <summary>
    /// A team whose agents read files carries a folder to read from.
    /// <para>
    /// The trial bench mounts the CLI's working directory as <c>/workspace:ro</c>, the
    /// Composer shows the chip, and nothing carried it into adoption: a <c>file_read</c> team
    /// passed its trial and could then read nothing — the mirror of the missing
    /// <c>/output</c>. It is a folder INSIDE the team, not the team's root, because
    /// <c>--with-settings</c> puts an <c>appsettings.json</c> holding API keys at that root.
    /// </para>
    /// </summary>
    [Fact]
    public void A_reading_team_carries_an_input_folder_mounted_read_only()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"crew":{"name":"veille"},
             "agents":[{"key":"l","role":"Lecteur","goal":"Lire","tools":["file_read","file_write"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"l","deliverable":"/output/r.md"}]}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        Assert.True(Directory.Exists(Path.Combine(Destination, "input")));

        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Contains("\\\"$DIR/input\\\":/workspace:ro", posix, StringComparison.Ordinal);
        Assert.Contains("\\\"$DIR/output\\\":/output:rw", posix, StringComparison.Ordinal);
        Assert.Equal(1, posix.Split("--mount").Length - 1);

        // The team's own root is NOT mounted: an appsettings.json there holds API keys.
        Assert.DoesNotContain(":/workspace:ro\"\n", posix.Replace("$DIR/input", "X", StringComparison.Ordinal), StringComparison.Ordinal);

        var card = File.ReadAllText(Path.Combine(Destination, ForgePromoter.CardFileName));
        // The brief of this fixture is French, so the card is too.
        Assert.Contains("`/workspace` lecture → `input/`", card, StringComparison.Ordinal);
        Assert.Contains("hors de portée des agents", card, StringComparison.Ordinal);
    }

    /// <summary>A team that reads nothing carries no input folder and no read mount.</summary>
    [Fact]
    public void A_team_that_reads_nothing_carries_no_input_folder()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"crew":{"name":"veille"},
             "agents":[{"key":"w","role":"Writer","goal":"Écrire","tools":["file_write"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"w","deliverable":"/output/r.md"}]}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        Assert.False(Directory.Exists(Path.Combine(Destination, "input")));
        Assert.DoesNotContain(
            "/workspace",
            File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The launcher's <c>--mount</c>, run through a real shell and parsed by the grammar that
    /// receives it, from a folder whose name carries the separator.
    /// <para>
    /// This is the assertion the string comparison above could never make. The spec was built
    /// by concatenation, so its physical segment inherited whatever the anchor expanded to —
    /// and on Windows that is always <c>C:\…</c>. Four segments, <c>FormatException</c>, every
    /// promoted team dead at start on the platform the <c>.cmd</c> launcher exists for. The
    /// suite proved the flag was present and never that it could be read.
    /// </para>
    /// </summary>
    [Fact]
    public void The_launcher_mount_survives_the_shell_and_the_grammar()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The POSIX launcher needs a POSIX shell.");

        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"crew":{"name":"veille"},
             "agents":[{"key":"c","role":"C","goal":"G","tools":["file_write"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"c","deliverable":"/output/r.md"}]}
            """));

        // A destination the mount grammar has to be told about: ':' is its own separator.
        var awkward = Path.Combine(_workspace, "te:am");
        ForgePromoter.Promote(
            session, awkward, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var launcher = Path.Combine(awkward, ForgePromoter.PosixLauncherName);
        var argv = RunThroughShell(launcher);

        var mountIndex = argv.IndexOf("--mount");
        Assert.True(mountIndex >= 0, $"the launcher passed no --mount: {string.Join(' ', argv)}");

        var spec = argv[mountIndex + 1];
        var mount = Orkeon.Domain.FileSystem.FileSystemMount.Parse(spec);

        Assert.Equal("/output", mount.VirtualPath);
        Assert.Equal(Path.Combine(awkward, "output"), Path.TrimEndingDirectorySeparator(mount.BasePath));
        Assert.Equal(Orkeon.Domain.FileSystem.FileAccessRights.ReadWrite, mount.DefaultRights);
    }

    /// <summary>
    /// Runs a generated <c>run.sh</c> with a stub <c>orkeon</c> on PATH that prints one
    /// argument per line, and returns the argument vector the real CLI would have received.
    /// </summary>
    private List<string> RunThroughShell(string launcherPath)
    {
        var binDir = Path.Combine(_workspace, "stub-bin");
        Directory.CreateDirectory(binDir);
        var stub = Path.Combine(binDir, "orkeon");
        File.WriteAllText(stub, "#!/usr/bin/env sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\"; done\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(launcherPath);
        startInfo.Environment["PATH"] = binDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"launcher exited {process.ExitCode}: {stderr}");
        return [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
    }

    [Fact]
    public void A_blueprint_without_deliverables_carries_no_mount()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"crew":{"name":"veille"},
             "agents":[{"key":"collecteur","role":"Collecteur","goal":"Trouver","tools":["web_scrape"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"collecteur"}]}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.DoesNotContain("--mount", posix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The launcher must run FROM the team's folder. The runner refuses to read a crew outside
    /// the working directory without <c>--allow-external-mounts</c>, and the security whitelist
    /// is rooted on the working directory too — so a launch from anywhere else (a scheduled task
    /// starts in the system directory, which is exactly what the schedule artifacts invoke) was
    /// refused outright, or ran and wrote nothing into <c>/output</c>.
    /// </summary>
    [Fact]
    public void Both_launchers_run_from_the_team_folder()
    {
        ForgePromoter.Promote(
            ReadySession(), Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Contains("cd \"$DIR\"", posix, StringComparison.Ordinal);
        Assert.True(
            posix.IndexOf("cd \"$DIR\"", StringComparison.Ordinal) < posix.IndexOf("exec orkeon", StringComparison.Ordinal),
            "the launcher must change directory before invoking orkeon");

        var windows = File.ReadAllText(Path.Combine(Destination, ForgePromoter.WindowsLauncherName));
        Assert.Contains("cd /d \"%~dp0\"", windows, StringComparison.Ordinal);
    }

    /// <summary>
    /// `--var` obeys the same single-flag rule as `--mount`: several values go space-separated
    /// after ONE flag. Spelled once per variable, a brief with two sample inputs promoted to a
    /// team the CLI's own parser refused with "option repeated".
    /// </summary>
    [Fact]
    public void Several_sample_variables_go_after_one_var_flag()
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BriefFileName, JsonSerializer.Deserialize<JsonElement>(
            """
            {"need":"n","language":"fr",
             "sample":{"variables":{"supplier_url":"https://exemple.fr/offres","date":"2026-08-26"}}}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        // "--var" also appears in the header comment, so count the argument lines themselves.
        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.Equal(1, posix.Split("\n  --var ").Length - 1);
        Assert.Contains("supplier_url=https://exemple.fr/offres", posix, StringComparison.Ordinal);
        Assert.Contains("date=2026-08-26", posix, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blueprint is LLM-authored: a deliverable under a root the runner reserves would make
    /// the promoted launcher spell a `--mount` the CLI refuses at start, and `/..` would mount
    /// the team's parent read-write.
    /// </summary>
    [Theory]
    [InlineData("/crew/summary.md")]
    [InlineData("/script/summary.md")]
    [InlineData("/llm-logs/summary.md")]
    [InlineData("/../summary.md")]
    public void A_deliverable_root_the_runner_reserves_is_not_mounted(string deliverable)
    {
        var session = ReadySession();
        session.SaveArtifact(ForgeSession.BlueprintFileName, JsonSerializer.Deserialize<JsonElement>(
            $$"""
            {"crew":{"name":"veille"},
             "agents":[{"key":"a","role":"A","goal":"g","tools":["file_write"]}],
             "tasks":[{"key":"t1","description":"d","expectedOutput":"e","agent":"a","deliverable":"{{deliverable}}"}]}
            """));

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var posix = File.ReadAllText(Path.Combine(Destination, ForgePromoter.PosixLauncherName));
        Assert.DoesNotContain("--mount", posix, StringComparison.Ordinal);
    }

    /// <summary>
    /// FORGE-09: the folder carries a machine-readable twin of the card — the brief and the
    /// session's identity — so <c>forge reopen</c> can rebuild a faithful session once the
    /// original is gone. A re-promotion overwrites it; nothing secret is in it.
    /// </summary>
    [Fact]
    public void The_promoted_folder_records_its_brief_and_identity_for_a_later_reopen()
    {
        var session = ReadySession();

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        var record = ForgeTeamRecord.TryRead(Destination);
        Assert.NotNull(record);
        Assert.Equal("veille", record.Slug);
        Assert.Equal("Résumer chaque matin les nouvelles offres", record.Title);
        Assert.Equal("yaml", record.Format);
        Assert.Equal("2026-08-19T10:00:00Z", record.PromotedAt);
        Assert.Equal("Résumer chaque matin les nouvelles offres du fournisseur", record.Brief!.Goal);
        Assert.Equal(["A1", "A2"], record.Brief.Acceptance!.Select(a => a.Id));

        // Absent or broken, the record is simply not there — never a throw.
        File.WriteAllText(Path.Combine(Destination, ForgeTeamRecord.FileName), "{not json");
        Assert.Null(ForgeTeamRecord.TryRead(Destination));
        Assert.Null(ForgeTeamRecord.TryRead(Path.Combine(Destination, "nowhere")));
    }

    /// <summary>
    /// STUDIO-25: the promotion copies the session's id into <c>forge.json</c> — what links the
    /// folder back to its session wherever the folder goes, and what tells a copy from it.
    /// </summary>
    [Fact]
    public void The_promotion_copies_the_session_id_into_the_team_record()
    {
        var session = ReadySession();

        ForgePromoter.Promote(
            session, Destination, schedule: null, settingsPath: null, copySettings: false,
            ForgePromotePlatform.Linux, Now);

        Assert.NotNull(session.Document.Id);
        Assert.Equal(session.Document.Id, ForgeTeamRecord.TryRead(Destination)?.SessionId);
        Assert.Equal(session.Document.Id, ForgeTeamRecord.ReadSessionId(Destination));
        Assert.Null(ForgeTeamRecord.ReadSessionId(Path.Combine(_workspace, "nowhere")));
    }

    // ── STUDIO-26: the team's name reaches the engine, and the session folder follows the team ──

    /// <summary>
    /// D-01/D-02: the name the user gave the team reaches the engine. It titles the card a
    /// colleague reads, the record <c>forge reopen</c> rebuilds from, and the session itself — all
    /// three carried the goal sentence of the interview before, whatever the team was called.
    /// </summary>
    [Fact]
    public async Task The_name_passed_to_promote_titles_the_card_the_record_and_the_session()
    {
        ReadySession();
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(
            ["promote", "veille", "--to", Destination, "--name", "Ma veille", "--events", "jsonl"], _workspace));

        Assert.Equal("# Ma veille", CardTitle(Destination));
        Assert.Equal("Ma veille", ForgeTeamRecord.TryRead(Destination)!.Title);
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "ma-veille", out var session, out _));
        Assert.Equal("Ma veille", session!.Document.Title);
    }

    /// <summary>
    /// D-02/D-03: once the promotion is written, the session folder takes the team folder's name —
    /// as it is — and the wire says so, so the atelier lists the session under the team it made
    /// rather than under the need it was opened with. Everything travels with the folder, the
    /// history included; the id does not change, so rule R still links the two.
    /// </summary>
    [Fact]
    public async Task Adoption_gives_the_session_folder_the_team_folders_name()
    {
        var session = ReadySession("resumer-chaque-matin-les-offres");
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(
            ["promote", "resumer-chaque-matin-les-offres", "--to", Destination, "--name", "Ma veille", "--events", "jsonl"],
            _workspace));

        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "promoted", "session.renamed", "session.finished"], Kinds(events));
        var moved = Path.Combine(ForgeSession.RootFor(_workspace), "ma-veille");
        var renamed = events[2];
        Assert.Equal("resumer-chaque-matin-les-offres", renamed.GetProperty("from").GetString());
        Assert.Equal("ma-veille", renamed.GetProperty("to").GetString());
        Assert.Equal(moved, renamed.GetProperty("dir").GetString());
        Assert.False(renamed.GetProperty("suffixed").GetBoolean());
        Assert.Equal(0, events[3].GetProperty("exitCode").GetInt32());

        // One folder in the atelier, named after the team; nothing is left under the old name.
        Assert.Equal(["ma-veille"], Directory.GetDirectories(ForgeSession.RootFor(_workspace)).Select(Path.GetFileName));
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "ma-veille", out var aligned, out _));
        Assert.Equal("ma-veille", aligned!.Document.Slug);
        Assert.Equal(session.Document.Id, aligned.Document.Id);
        Assert.Equal(ForgeSessionStatus.Promoted, aligned.Status);
        Assert.Equal(Destination, aligned.Document.PromotedTo);
        Assert.Contains("\"trigger\":\"Promote\"",
            await File.ReadAllTextAsync(Path.Combine(moved, ForgeSession.HistoryFileName), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        // The team's record names the session by its new slug, and rule R still links them.
        Assert.Equal("ma-veille", ForgeTeamRecord.TryRead(Destination)!.Slug);
        var link = ForgeTeamLink.Resolve(_workspace, Destination);
        Assert.Equal(TeamSessionLinkKind.Linked, link.Kind);
        Assert.Equal(moved, link.Session!.Directory);
    }

    /// <summary>
    /// D-04: another session already holds the team folder's name — a second team forged under the
    /// same one. It is never overwritten: the adopting session takes the name suffixed -2, and the
    /// event says the name was taken.
    /// </summary>
    [Fact]
    public async Task An_alignment_collision_is_suffixed_and_reported()
    {
        var other = ForgeSession.Create(_workspace, "ma-veille");
        ReadySession();
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "veille", "--to", Destination, "--events", "jsonl"], _workspace));

        var renamed = Events(console.Stdout).Single(e => e.GetProperty("kind").GetString() == "session.renamed");
        Assert.Equal("veille", renamed.GetProperty("from").GetString());
        Assert.Equal("ma-veille-2", renamed.GetProperty("to").GetString());
        Assert.True(renamed.GetProperty("suffixed").GetBoolean());
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "ma-veille-2", out var aligned, out _));
        Assert.Equal(ForgeSessionStatus.Promoted, aligned!.Status);
        Assert.Equal("ma-veille-2", ForgeTeamRecord.TryRead(Destination)!.Slug);

        // The other session is exactly what and where it was.
        Assert.True(ForgeSession.TryLoad(other.Directory, out var untouched, out _));
        Assert.Equal(other.Document.Id, untouched!.Document.Id);
        Assert.Equal("ma-veille", untouched.Document.Slug);
        Assert.Equal(ForgeSessionStatus.Active, untouched.Status);
    }

    /// <summary>
    /// A re-adoption finds its session already named after the team: nothing moves and nothing is
    /// said — the folder is no collision with itself.
    /// </summary>
    [Fact]
    public async Task A_session_already_named_after_its_team_stays_where_it_is()
    {
        ReadySession("ma-veille");
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "ma-veille", "--to", Destination, "--events", "jsonl"], _workspace));

        Assert.Equal(["session.started", "promoted", "session.finished"], Kinds(Events(console.Stdout)));
        Assert.Equal(["ma-veille"], Directory.GetDirectories(ForgeSession.RootFor(_workspace)).Select(Path.GetFileName));
    }

    /// <summary>
    /// D-05: a move the disk refuses leaves the adoption as it stands. The promotion is written and
    /// the command succeeds; the stream carries a structured warning — never an error, never
    /// silence — and the session keeps its folder, linked to its team by the id all the same.
    /// <para>
    /// The refusal is one every file system makes whoever runs the test — root included, which
    /// ignores the permissions a read-only folder would rely on: a team folder named at the
    /// 255-character limit, whose name another session already holds, can only be followed under a
    /// name one past that limit once suffixed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_move_the_disk_refuses_leaves_the_adoption_successful_with_a_warning()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A 255-character folder name under the temp folder needs long paths on Windows.");

        var session = ReadySession();
        var atTheLimit = new string('v', 255);
        Directory.CreateDirectory(Path.Combine(ForgeSession.RootFor(_workspace), atTheLimit));
        var team = Path.Combine(_workspace, "teams", atTheLimit);
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "veille", "--to", team, "--events", "jsonl"], _workspace));

        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "promoted", "warning", "session.finished"], Kinds(events));
        Assert.Equal(ForgeErrorCodes.SessionNotRenamed, events[2].GetProperty("code").GetString());
        Assert.Contains("'veille'", events[2].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("ready", events[3].GetProperty("status").GetString());
        Assert.Equal(0, events[3].GetProperty("exitCode").GetInt32());

        // Promoted where it stands, its team's record unchanged: the id links the two.
        Assert.True(ForgeSession.TryLoad(session.Directory, out var kept, out _));
        Assert.Equal(ForgeSessionStatus.Promoted, kept!.Status);
        Assert.Equal("veille", kept.Document.Slug);
        Assert.Equal("veille", ForgeTeamRecord.TryRead(team)!.Slug);
        Assert.Equal(TeamSessionLinkKind.Linked, ForgeTeamLink.Resolve(_workspace, team).Kind);
    }

    /// <summary>
    /// D-06: every artifact the promotion generates carries the team folder's name, never the slug
    /// of the need the session was opened with — the unit files and their descriptions, the
    /// scheduled task, the install command of each platform, the launchers' header — and so does
    /// the <c>install</c> field of the <c>promoted</c> event.
    /// </summary>
    [Fact]
    public async Task The_schedule_artifacts_and_the_install_command_carry_the_team_name()
    {
        var session = ReadySession("resumer-chaque-matin-les-offres");
        Assert.True(ForgeSchedule.TryParse("daily@07:30", out var daily, out _));

        foreach (var platform in new[] { ForgePromotePlatform.Linux, ForgePromotePlatform.Windows, ForgePromotePlatform.Other })
        {
            var team = Path.Combine(_workspace, platform.ToString(), "ma-veille");
            var result = ForgePromoter.Promote(session, team, daily, null, false, platform, Now);

            var scheduleDir = Path.Combine(team, ForgePromoter.ScheduleDirectoryName);
            Assert.Equal(
                ["cron.txt", "orkeon-ma-veille.service", "orkeon-ma-veille.timer", "windows-task.xml"],
                Directory.GetFiles(scheduleDir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Contains("Description=Orkeon crew 'ma-veille'",
                await File.ReadAllTextAsync(Path.Combine(scheduleDir, "orkeon-ma-veille.service"), TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
            Assert.Contains("Description=Schedule for Orkeon crew 'ma-veille'",
                await File.ReadAllTextAsync(Path.Combine(scheduleDir, "orkeon-ma-veille.timer"), TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
            Assert.Contains("ma-veille", result.InstallCommand, StringComparison.Ordinal);
            Assert.DoesNotContain("resumer", result.InstallCommand, StringComparison.Ordinal);
            foreach (var generated in Directory.GetFiles(scheduleDir)
                         .Append(Path.Combine(team, ForgePromoter.PosixLauncherName))
                         .Append(Path.Combine(team, ForgePromoter.WindowsLauncherName)))
            {
                Assert.DoesNotContain("resumer",
                    await File.ReadAllTextAsync(generated, TestContext.Current.CancellationToken), StringComparison.Ordinal);
            }

            Assert.Contains("for the team 'ma-veille'",
                await File.ReadAllTextAsync(Path.Combine(team, ForgePromoter.PosixLauncherName), TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
            Assert.Contains("for the team 'ma-veille'",
                await File.ReadAllTextAsync(Path.Combine(team, ForgePromoter.WindowsLauncherName), TestContext.Current.CancellationToken),
                StringComparison.Ordinal);
        }

        Assert.Contains("systemctl --user enable --now orkeon-ma-veille.timer",
            ForgePromoter.Promote(session, Path.Combine(_workspace, "linux-2", "ma-veille"), daily, null, false, ForgePromotePlatform.Linux, Now).InstallCommand,
            StringComparison.Ordinal);
        Assert.StartsWith("schtasks /Create /TN \"Orkeon ma-veille\"",
            ForgePromoter.Promote(session, Path.Combine(_workspace, "windows-2", "ma-veille"), daily, null, false, ForgePromotePlatform.Windows, Now).InstallCommand,
            StringComparison.Ordinal);

        // Through the verb: the event's install command names the team on whatever platform runs it.
        using var console = new TestConsole();
        Assert.Equal(0, await ForgeCommand.DispatchAsync(
            ["promote", "resumer-chaque-matin-les-offres", "--to", Destination, "--schedule", "daily@07:30", "--events", "jsonl"],
            _workspace));
        var install = Events(console.Stdout).Single(e => e.GetProperty("kind").GetString() == "promoted").GetProperty("install").GetString();
        Assert.Contains("ma-veille", install, StringComparison.Ordinal);
        Assert.DoesNotContain("resumer", install, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder whose name a unit file cannot carry — spaces, capitals — names its artifacts through
    /// the one folder rule: what systemd and schtasks accept, and for every folder Studio names, the
    /// folder's name itself.
    /// </summary>
    [Fact]
    public void A_folder_name_a_unit_cannot_carry_names_its_artifacts_through_the_folder_rule()
    {
        Assert.Equal("ma-veille", ForgePromoter.ArtifactName(Destination));
        Assert.Equal("ma-veille-v2", ForgePromoter.ArtifactName(Path.Combine(_workspace, "teams", "Ma Veille (v2)")));
        Assert.Equal("ma-veille", ForgePromoter.ArtifactName(Destination + Path.DirectorySeparatorChar));
        Assert.Equal(FolderSlug.TeamFallback, ForgePromoter.ArtifactName(Path.Combine(_workspace, "teams", "每日监控")));
    }

    /// <summary>
    /// D-01: a team's name is the user's words, and a leading dash is one of them — the value of
    /// <c>--name</c> is taken as written, where any other option's would be read as the next option.
    /// </summary>
    [Fact]
    public async Task A_name_starting_with_a_dash_is_accepted()
    {
        var options = ForgeCommandOptions.Parse(["promote", "veille", "--to", "/t", "--name", "-Veille-", "--events", "jsonl"]);
        Assert.Null(options.Error);
        Assert.Equal("-Veille-", options.TeamName);
        Assert.Equal("/t", options.Destination);
        Assert.True(options.Events);

        ReadySession();
        using var console = new TestConsole();
        Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "veille", "--to", Destination, "--name", "-Veille-"], _workspace));
        Assert.Equal("# -Veille-", CardTitle(Destination));
    }

    /// <summary>
    /// The name's grammar, loud like the rest of the parser: a missing or blank name is refused, a
    /// name anywhere but on <c>promote</c> would be ignored and is refused, and a line break in it
    /// would split the card's title in two, so it reaches the engine on one line.
    /// </summary>
    [Fact]
    public void The_name_is_refused_where_it_means_nothing()
    {
        Assert.Contains("--name needs the team's name", ForgeCommandOptions.Parse(["promote", "veille", "--to", "/t", "--name"]).Error, StringComparison.Ordinal);
        Assert.Contains("--name needs the team's name", ForgeCommandOptions.Parse(["promote", "veille", "--to", "/t", "--name", "  "]).Error, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", ForgeCommandOptions.Parse(["une veille", "--name", "Ma veille"]).Error, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", ForgeCommandOptions.Parse(["resume", "veille", "--name", "Ma veille"]).Error, StringComparison.Ordinal);
        Assert.Equal("Ma veille", ForgeCommandOptions.Parse(["promote", "veille", "--to", "/t", "--name", "Ma\nveille "]).TeamName);
    }

    /// <summary>The first line of the card a promotion wrote into <paramref name="team"/>.</summary>
    private static string CardTitle(string team) =>
        File.ReadAllText(Path.Combine(team, ForgePromoter.CardFileName)).ReplaceLineEndings("\n").Split('\n')[0];

    private static List<JsonElement> Events(string stdout) => stdout
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonElement.Parse(line))
        .ToList();

    private static List<string?> Kinds(IEnumerable<JsonElement> events) =>
        [.. events.Select(e => e.GetProperty("kind").GetString())];
}
