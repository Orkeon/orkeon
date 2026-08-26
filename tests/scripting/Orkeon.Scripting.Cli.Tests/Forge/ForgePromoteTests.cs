using System.Reflection;
using System.Text.Json;
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

    private string Destination => Path.Combine(_workspace, "promoted");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ForgeSession ReadySession(bool withVerdict = true)
    {
        var session = ForgeSession.Create(_workspace, "veille");
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
            File.ReadAllText(Path.Combine(scheduleDir, "orkeon-veille.timer")), StringComparison.Ordinal);
        Assert.Contains("run.sh", File.ReadAllText(Path.Combine(scheduleDir, "orkeon-veille.service")), StringComparison.Ordinal);

        // 10:00 > 07:30 on the fixed clock: the Windows task anchors on tomorrow's occurrence.
        var task = File.ReadAllText(Path.Combine(scheduleDir, "windows-task.xml"));
        Assert.Contains("<StartBoundary>2026-08-20T07:30:00</StartBoundary>", task, StringComparison.Ordinal);
        Assert.Contains("run.cmd", task, StringComparison.Ordinal);

        Assert.Equal(ForgePromoter.ScheduleDirectoryName, result.ScheduleDirectory);
        Assert.Contains("systemctl --user enable --now orkeon-veille.timer", result.InstallCommand, StringComparison.Ordinal);

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
            File.ReadAllText(Path.Combine(scheduleDir, "orkeon-veille.timer")), StringComparison.Ordinal);
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

        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal(["session.started", "promoted", "session.finished"],
            events.Select(e => e.GetProperty("kind").GetString()).ToList());
        Assert.Equal(Destination, events[1].GetProperty("path").GetString());
        Assert.False(events[1].TryGetProperty("schedule", out _));
        Assert.Equal("ready", events[2].GetProperty("status").GetString());

        // The session moved to Promoted, transition in the history, and refuses a second run.
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var promoted, out _));
        Assert.Equal(ForgeState.Promoted, promoted!.State);
        Assert.Equal(ForgeSessionStatus.Promoted, promoted.Status);
        Assert.Equal(Destination, promoted.Document.PromotedTo);
        Assert.Contains("\"trigger\":\"Promote\"",
            await File.ReadAllTextAsync(Path.Combine(promoted.Directory, ForgeSession.HistoryFileName), TestContext.Current.CancellationToken),
            StringComparison.OrdinalIgnoreCase);

        using var again = new TestConsole();
        Assert.Equal(1, await ForgeCommand.DispatchAsync(["promote", "veille", "--to", Destination], _workspace));
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
            ["promote", "veille", "--to", Destination, "--events", "jsonl"], _workspace);

        Assert.Equal(1, exitCode);
        var error = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeErrorCodes.PromoteFailed, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("recoverable").GetBoolean());

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille", out var session, out _));
        Assert.Equal(ForgeSessionStatus.Ready, session!.Status);
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
}
