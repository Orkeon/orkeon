using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>forge rename &lt;team-folder&gt; --name &lt;name&gt;</c> (STUDIO-28, D-01): the team folder takes
/// the folder rule's name, the linked session's folder follows it, every title and generated file
/// says the new name and the new path, and an installed schedule is reinstalled under it — all of
/// it, or nothing: a step that fails puts back everything done before it. The operating system is
/// a hand-written double; nothing here runs schtasks, systemctl or crontab.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeRenameTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-rename-" + Guid.NewGuid().ToString("N"));

    /// <summary>A team folder the way Studio names one.</summary>
    private string Team => Path.Combine(_workspace, "teams", "ma-veille");

    /// <summary>Where « Veille du matin » goes: the folder rule's name, next to the team.</summary>
    private string Renamed => Path.Combine(_workspace, "teams", "veille-du-matin");

    /// <summary>The user unit directory the systemd adapter writes into — never the real one.</summary>
    private string UnitDirectory => Path.Combine(_workspace, "home", ".config", "systemd", "user");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    public static TheoryData<string> Families => ["windows", "linux", "other"];

    private static ForgePromotePlatform Platform(string family) => family switch
    {
        "windows" => ForgePromotePlatform.Windows,
        "linux" => ForgePromotePlatform.Linux,
        _ => ForgePromotePlatform.Other,
    };

    // ── the rename ──

    /// <summary>
    /// D-01: the team folder and the linked session's folder both take the new name, and every
    /// title follows — session.json, forge.json, studio-team.json (whose other fields, known or
    /// not, stay as they were), FORGE.md — while the launchers' header names the new folder. The
    /// id does not change, so rule R still links the two.
    /// </summary>
    [Fact]
    public async Task Renaming_moves_both_folders_and_updates_every_title()
    {
        var session = Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        var (_, host) = Machine(ForgePromotePlatform.Linux);

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Veille du matin");

        Assert.Equal(0, exitCode);
        Assert.Equal(["session.renamed", "team.renamed"], Kinds(events));
        var movedSession = Path.Combine(ForgeSession.RootFor(_workspace), "veille-du-matin");
        Assert.Equal("ma-veille", events[0].GetProperty("from").GetString());
        Assert.Equal("veille-du-matin", events[0].GetProperty("to").GetString());
        Assert.Equal(movedSession, events[0].GetProperty("dir").GetString());
        Assert.False(events[0].GetProperty("suffixed").GetBoolean());
        Assert.Equal(Team, events[1].GetProperty("from").GetString());
        Assert.Equal(Renamed, events[1].GetProperty("path").GetString());
        Assert.Equal("Veille du matin", events[1].GetProperty("name").GetString());

        // One team folder, one session folder, both under the new name.
        Assert.False(Directory.Exists(Team));
        Assert.True(Directory.Exists(Renamed));
        Assert.Equal(["veille-du-matin"], Directory.GetDirectories(ForgeSession.RootFor(_workspace)).Select(Path.GetFileName));

        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "veille-du-matin", out var renamed, out _));
        Assert.Equal(session.Document.Id, renamed!.Document.Id);
        Assert.Equal("veille-du-matin", renamed.Document.Slug);
        Assert.Equal("Veille du matin", renamed.Document.Title);
        Assert.Equal(Renamed, renamed.Document.PromotedTo);
        Assert.Equal(ForgeSessionStatus.Promoted, renamed.Status);

        var record = ForgeTeamRecord.TryRead(Renamed)!;
        Assert.Equal(session.Document.Id, record.SessionId);
        Assert.Equal("veille-du-matin", record.Slug);
        Assert.Equal("Veille du matin", record.Title);

        var sidecar = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Renamed, ConventionalNames.StudioTeamFile), TestContext.Current.CancellationToken))!;
        Assert.Equal("Veille du matin", sidecar["name"]!.GetValue<string>());
        Assert.Equal("Local", sidecar["profile"]!.GetValue<string>());
        Assert.Equal("./output:/output:rw", sidecar["mounts"]![0]!.GetValue<string>());
        Assert.Equal("2026-09-20T08:00:00Z", sidecar["lastRunAt"]!.GetValue<string>());
        Assert.False(sidecar["archived"]!.GetValue<bool>());

        Assert.Equal("# Veille du matin", CardTitle(Renamed));
        foreach (var launcher in new[] { ForgePromoter.PosixLauncherName, ForgePromoter.WindowsLauncherName })
        {
            var text = await File.ReadAllTextAsync(Path.Combine(Renamed, launcher), TestContext.Current.CancellationToken);
            Assert.Contains("for the team 'veille-du-matin'", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ma-veille", text, StringComparison.Ordinal);
        }

        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(Renamed, ForgePromoter.PosixLauncherName)).HasFlag(UnixFileMode.UserExecute));

        var link = ForgeTeamLink.Resolve(_workspace, Renamed);
        Assert.Equal(TeamSessionLinkKind.Linked, link.Kind);
        Assert.Equal(movedSession, link.Session!.Directory);
    }

    /// <summary>
    /// D-01 step 5: the team's schedule/ is regenerated for the folder as it is now — every unit,
    /// task and cron line names the new folder's launchers under the new name — and so is the
    /// install command FORGE.md shows. A schedule declared and never installed asks the OS nothing.
    /// </summary>
    [Fact]
    public async Task The_schedule_artifacts_follow_the_new_path_and_the_new_name()
    {
        Adopt(Team, ForgePromotePlatform.Linux);
        var (os, host) = Machine(ForgePromotePlatform.Linux);

        Assert.Equal(0, (await RunAsync(host, "rename", Team, "--name", "Veille du matin")).ExitCode);

        var schedule = Path.Combine(Renamed, ForgePromoter.ScheduleDirectoryName);
        Assert.Equal(
            ["cron.txt", "orkeon-veille-du-matin.service", "orkeon-veille-du-matin.timer", "windows-task.xml"],
            Directory.GetFiles(schedule).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains($"ExecStart=\"{Path.Combine(Renamed, "run.sh")}\"",
            await File.ReadAllTextAsync(Path.Combine(schedule, "orkeon-veille-du-matin.service"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Contains($"<Command>{Path.Combine(Renamed, "run.cmd")}</Command>",
            await File.ReadAllTextAsync(Path.Combine(schedule, "windows-task.xml"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Contains($"\"{Path.Combine(Renamed, "run.sh")}\" # orkeon:veille-du-matin",
            await File.ReadAllTextAsync(Path.Combine(schedule, "cron.txt"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        foreach (var file in Directory.GetFiles(schedule))
            Assert.DoesNotContain("ma-veille", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        var card = await File.ReadAllTextAsync(Path.Combine(Renamed, ForgePromoter.CardFileName), TestContext.Current.CancellationToken);
        Assert.Contains(ForgeScheduleAdapters.ManualInstallCommand(ForgePromotePlatform.Linux, Renamed, "veille-du-matin"), card, StringComparison.Ordinal);
        Assert.DoesNotContain("ma-veille", card, StringComparison.Ordinal);

        Assert.Empty(os.Invocations);
    }

    /// <summary>
    /// D-01 step 5: a schedule the OS runs is reinstalled under the new name — the registration of
    /// the former name removed, the new one running the new folder's launcher, one registration in
    /// all — and forge.json records what is installed now.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task An_installed_schedule_is_reinstalled_under_the_new_name(string family)
    {
        var platform = Platform(family);
        Adopt(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Veille du matin");

        Assert.Equal(0, exitCode);
        Assert.Equal(["session.renamed", "schedule.state", "team.renamed"], Kinds(events));
        Assert.Equal("installed", events[1].GetProperty("state").GetString());
        Assert.False(IsRegistered(os, platform, "ma-veille", Team));
        Assert.True(IsRegistered(os, platform, "veille-du-matin", Renamed));
        Assert.Equal(1, RegistrationCount(os, platform));

        var installed = ForgeTeamRecord.TryRead(Renamed)!.Schedule!.Installed!;
        Assert.Equal(Renamed, installed.Path);
        Assert.Equal(ExpectedNames(platform, "veille-du-matin"), installed.Names);
        Assert.Equal("installed", Assert.Single((await RunAsync(host, "schedule", Renamed, "--check")).Events).GetProperty("state").GetString());
    }

    // ── all or nothing ──

    /// <summary>
    /// D-01: the OS refuses the new registration — after the former one was removed. Everything is
    /// put back: both folders under their names, every file as it was to the byte, the former
    /// registration running the former launcher. The error says the team was not renamed and what
    /// the OS said; it offers no command by hand — any would name a folder that does not exist.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_refused_reinstall_puts_everything_back_as_it_was(string family)
    {
        var platform = Platform(family);
        Adopt(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        var before = Tree();
        var registered = Registrations(os, platform);
        os.RefuseWhen = invocation => NamesTheNewRegistration(invocation) ? "Access is denied." : null;

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Veille du matin");

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal("error", Kind(error));
        Assert.Equal(ForgeErrorCodes.ScheduleRefused, error.GetProperty("code").GetString());
        Assert.Contains("not renamed", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Access is denied.", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(error.TryGetProperty("command", out _));

        Assert.Equal(before, Tree());
        Assert.Equal(registered, Registrations(os, platform));
        Assert.True(IsRegistered(os, platform, "ma-veille", Team));
        Assert.Equal(1, RegistrationCount(os, platform));
    }

    /// <summary>
    /// D-01: the disk refuses midway — the team's schedule/ cannot be regenerated because a file
    /// sits where the folder goes — after both folders moved and every title was rewritten. All of
    /// it is undone: the tree is exactly what it was, and nothing carries the new name.
    /// </summary>
    [Fact]
    public async Task A_disk_failure_midway_puts_everything_back_as_it_was()
    {
        Adopt(Team, ForgePromotePlatform.Linux);
        var schedule = Path.Combine(Team, ForgePromoter.ScheduleDirectoryName);
        Directory.Delete(schedule, recursive: true);
        await File.WriteAllTextAsync(schedule, "not a folder", TestContext.Current.CancellationToken);
        var (_, host) = Machine(ForgePromotePlatform.Linux);
        var before = Tree();

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Veille du matin");

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal(ForgeErrorCodes.RenameFailed, error.GetProperty("code").GetString());
        Assert.Contains("not renamed", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, Tree());
        Assert.False(Directory.Exists(Renamed));
    }

    // ── what the team has, or has not ──

    /// <summary>
    /// An imported team has no session and no forge.json: its folder and its sidecar are renamed
    /// all the same, and no record is invented. One whose session is gone keeps its record, retitled.
    /// </summary>
    [Fact]
    public async Task A_team_without_a_session_is_renamed_too()
    {
        var imported = Path.Combine(_workspace, "teams", "import");
        Directory.CreateDirectory(Path.Combine(imported, "crew"));
        await File.WriteAllTextAsync(Path.Combine(imported, "crew", "config.yaml"), "name: import\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(imported, ConventionalNames.StudioTeamFile), """{"name":"Import"}""", TestContext.Current.CancellationToken);
        var (_, host) = Machine(ForgePromotePlatform.Linux);

        var (exitCode, events) = await RunAsync(host, "rename", imported, "--name", "Veille importée");

        Assert.Equal(0, exitCode);
        Assert.Equal(["team.renamed"], Kinds(events));
        var moved = Path.Combine(_workspace, "teams", "veille-importee");
        Assert.False(Directory.Exists(imported));
        Assert.Equal("Veille importée",
            JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(moved, ConventionalNames.StudioTeamFile), TestContext.Current.CancellationToken))!["name"]!.GetValue<string>());
        Assert.False(File.Exists(Path.Combine(moved, ForgeTeamRecord.FileName)));
        Assert.True(File.Exists(Path.Combine(moved, "crew", "config.yaml")));

        // A session deleted since: the record stays, retitled, and a rebuild names its session after the team.
        var session = Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        Directory.Delete(session.Directory, recursive: true);
        Assert.Equal(["team.renamed"], Kinds((await RunAsync(host, "rename", Team, "--name", "Veille du matin")).Events));
        var record = ForgeTeamRecord.TryRead(Renamed)!;
        Assert.Equal("Veille du matin", record.Title);
        Assert.Equal("veille-du-matin", record.Slug);
        Assert.Equal(session.Document.Id, record.SessionId);
    }

    /// <summary>
    /// D-03: the new name's folder is already there — another team, a folder, a file. The rename is
    /// refused, saying what occupies it, and nothing moves.
    /// </summary>
    [Fact]
    public async Task A_taken_name_is_refused_and_says_what_holds_it()
    {
        var session = Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        Directory.CreateDirectory(Renamed);
        await File.WriteAllTextAsync(Path.Combine(Renamed, ConventionalNames.StudioTeamFile), """{"name":"Veille du matin"}""", TestContext.Current.CancellationToken);
        var (_, host) = Machine(ForgePromotePlatform.Linux);
        var before = Tree();

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Veille du matin");

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal(ForgeErrorCodes.RenameTaken, error.GetProperty("code").GetString());
        Assert.Contains(Renamed, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("another team", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, Tree());
        Assert.True(ForgeSession.TryLoad(session.Directory, out _, out _));

        var file = Path.Combine(_workspace, "teams", "une-note");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        var refused = Assert.Single((await RunAsync(host, "rename", Team, "--name", "Une note")).Events);
        Assert.Contains("a file", refused.GetProperty("message").GetString(), StringComparison.Ordinal);

        var missing = Assert.Single((await RunAsync(host, "rename", Path.Combine(_workspace, "nowhere"), "--name", "X")).Events);
        Assert.Equal(ForgeErrorCodes.TeamUnreadable, missing.GetProperty("code").GetString());
    }

    /// <summary>
    /// A copy carries its original's id and is linked to no session (rule R): renaming it moves the
    /// copy and retitles it, and leaves the original's session exactly where and what it was.
    /// </summary>
    [Fact]
    public async Task Renaming_a_copy_never_touches_its_originals_session()
    {
        var session = Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        var copy = Path.Combine(_workspace, "teams", "ma-veille-copy");
        ForgePromoter.CopyDirectory(Team, copy);
        var (_, host) = Machine(ForgePromotePlatform.Linux);

        var (exitCode, events) = await RunAsync(host, "rename", copy, "--name", "Veille copiée");

        Assert.Equal(0, exitCode);
        Assert.Equal(["team.renamed"], Kinds(events));
        var moved = Path.Combine(_workspace, "teams", "veille-copiee");
        Assert.Equal("Veille copiée", ForgeTeamRecord.TryRead(moved)!.Title);
        Assert.True(ForgeSession.TryLoad(session.Directory, out var original, out _));
        Assert.Equal("ma-veille", original!.Document.Slug);
        Assert.Equal("Ma veille", original.Document.Title);
        Assert.Equal(Team, original.Document.PromotedTo);
        Assert.Equal(TeamSessionLinkKind.Copy, ForgeTeamLink.Resolve(_workspace, moved).Kind);
    }

    /// <summary>A new name whose folder is the team's own only retitles: nothing moves, no session is renamed.</summary>
    [Fact]
    public async Task A_name_with_the_same_folder_only_retitles()
    {
        var session = Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        var (_, host) = Machine(ForgePromotePlatform.Linux);

        var (exitCode, events) = await RunAsync(host, "rename", Team, "--name", "Ma Veille !");

        Assert.Equal(0, exitCode);
        var renamed = Assert.Single(events);
        Assert.Equal("team.renamed", Kind(renamed));
        Assert.Equal(Team, renamed.GetProperty("from").GetString());
        Assert.Equal(Team, renamed.GetProperty("path").GetString());
        Assert.Equal("# Ma Veille !", CardTitle(Team));
        Assert.True(ForgeSession.TryLoad(session.Directory, out var retitled, out _));
        Assert.Equal("Ma Veille !", retitled!.Document.Title);
    }

    // ── the grammar, the terminal ──

    /// <summary>
    /// The verb takes the team folder and the name — as written, a leading dash included — and
    /// nothing but --events: it renames and starts nothing. --name stays refused where it means nothing.
    /// </summary>
    [Fact]
    public void The_rename_grammar_takes_the_folder_and_the_name()
    {
        var options = ForgeCommandOptions.Parse(["rename", "/teams/veille", "--name", "-Veille-", "--events", "jsonl"]);
        Assert.Null(options.Error);
        Assert.Equal("/teams/veille", options.RenameDirectory);
        Assert.Equal("-Veille-", options.TeamName);
        Assert.True(options.Events);

        Assert.Contains("needs the team folder", ForgeCommandOptions.Parse(["rename"]).Error, StringComparison.Ordinal);
        Assert.Contains("needs the team folder", ForgeCommandOptions.Parse(["rename", "--name", "X"]).Error, StringComparison.Ordinal);
        Assert.Contains("rename needs --name", ForgeCommandOptions.Parse(["rename", "/t"]).Error, StringComparison.Ordinal);
        Assert.Contains("--name needs the team's name", ForgeCommandOptions.Parse(["rename", "/t", "--name", " "]).Error, StringComparison.Ordinal);
        Assert.Contains("rename takes no option but --name and --events", ForgeCommandOptions.Parse(["rename", "/t", "--name", "X", "--dry"]).Error, StringComparison.Ordinal);
        Assert.Contains("rename takes no option but --name and --events", ForgeCommandOptions.Parse(["rename", "/t", "--name", "X", "de trop"]).Error, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", ForgeCommandOptions.Parse(["rename", "/t", "--name", "X", "--to", "/d"]).Error, StringComparison.Ordinal);
        Assert.Contains("--read only applies", ForgeCommandOptions.Parse(["rename", "/t", "--name", "X", "--read", "/d"]).Error, StringComparison.Ordinal);
        Assert.Contains("--check only applies", ForgeCommandOptions.Parse(["rename", "/t", "--name", "X", "--check"]).Error, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", ForgeCommandOptions.Parse(["reopen", "/t", "--name", "X"]).Error, StringComparison.Ordinal);
        // Only the first argument is a verb.
        Assert.Equal("veille rename", ForgeCommandOptions.Parse(["veille", "rename"]).Need);
    }

    /// <summary>Without <c>--events</c> the verb speaks to a person: one line per thing it did, in words.</summary>
    [Fact]
    public async Task Without_events_the_rename_says_it_in_words()
    {
        Adopt(Team, ForgePromotePlatform.Linux, schedule: null);
        var (_, host) = Machine(ForgePromotePlatform.Linux);
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["rename", Team, "--name", "Veille du matin"], _workspace, host));

        var lines = console.Stdout.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        Assert.Equal("  session renamed after the team: ma-veille → veille-du-matin", lines[0]);
        Assert.Equal($"✔ renamed “Veille du matin”: {Team} → {Renamed}", lines[1]);
    }

    // ── helpers ──

    private ForgeSession ReadySession(string slug = "veille")
    {
        var session = ForgeSession.Create(_workspace, slug, now: Now);
        session.Document.Title = "Ma veille";
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);

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
    /// A team adopted the way Studio adopts one: promoted into <paramref name="team"/> as
    /// <paramref name="platform"/> would, the session promoted and its folder named after the
    /// team's (STUDIO-26), and Studio's sidecar beside the crew — with a field no version of the
    /// CLI knows, which a rename must carry over untouched.
    /// </summary>
    private ForgeSession Adopt(string team, ForgePromotePlatform platform, string? schedule = "daily@07:30")
    {
        ForgeSchedule? parsed = null;
        if (schedule is not null)
            Assert.True(ForgeSchedule.TryParse(schedule, out parsed, out _));

        var session = ReadySession();
        ForgePromoter.Promote(session, team, parsed, settingsPath: null, copySettings: false, platform, Now);
        session.AppendHistory(ForgeState.Ready, ForgeTrigger.Promote, ForgeState.Promoted, Now);
        session.SetState(ForgeState.Promoted);
        session.SetStatus(ForgeSessionStatus.Promoted);
        session.Document.PromotedTo = team;
        session.Save(Now);
        var aligned = ForgeSessionFolder.FollowTeam(session, team, new ForgeEventWriter(TextWriter.Null), Now);

        File.WriteAllText(Path.Combine(team, ConventionalNames.StudioTeamFile),
            """
            {
              "name": "Ma veille",
              "description": "Résumer chaque matin les nouvelles offres",
              "profile": "Local",
              "schedule": "daily@07:30",
              "mounts": [ "./output:/output:rw" ],
              "archived": false,
              "lastRunAt": "2026-09-20T08:00:00Z"
            }
            """);
        return aligned;
    }

    private (FakeScheduleOs Os, ForgeScheduleHost Host) Machine(ForgePromotePlatform platform)
    {
        var os = new FakeScheduleOs(UnitDirectory);
        return (os, new ForgeScheduleHost(ForgeScheduleAdapters.For(platform, os, UnitDirectory), () => Now));
    }

    private async Task<(int ExitCode, List<JsonElement> Events)> RunAsync(ForgeScheduleHost host, params string[] args)
    {
        using var console = new TestConsole();
        var exitCode = await ForgeCommand.DispatchAsync([.. args, "--events", "jsonl"], _workspace, host);
        return (exitCode, Events(console.Stdout));
    }

    /// <summary>Every file and folder under the workspace, with its bytes: what « as it was » means.</summary>
    private List<string> Tree() =>
    [
        .. Directory.EnumerateFileSystemEntries(_workspace, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_workspace, path)
                + (File.Exists(path) ? " " + Convert.ToBase64String(File.ReadAllBytes(path)) : "/"))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Whether an OS command registers the new name — the one a refusal test refuses.</summary>
    private static bool NamesTheNewRegistration(FakeScheduleOs.Invocation invocation) => invocation.FileName switch
    {
        WindowsTaskScheduleAdapter.Program => invocation.Arguments.Contains("/Create") && invocation.Arguments.Contains("Orkeon veille-du-matin"),
        SystemdUserScheduleAdapter.Program => invocation.Arguments.Contains("enable") && invocation.Arguments.Contains("orkeon-veille-du-matin.timer"),
        _ => invocation.StandardInput?.Contains("# orkeon:veille-du-matin", StringComparison.Ordinal) == true,
    };

    /// <summary>What the OS holds, spelled so two moments can be compared.</summary>
    private List<string> Registrations(FakeScheduleOs os, ForgePromotePlatform platform) => platform switch
    {
        ForgePromotePlatform.Windows => [.. os.Tasks.Select(task => $"{task.Key}={task.Value}").Order(StringComparer.Ordinal)],
        ForgePromotePlatform.Linux =>
        [
            .. os.EnabledUnits.Order(StringComparer.Ordinal),
            .. (Directory.Exists(UnitDirectory) ? Directory.GetFiles(UnitDirectory) : [])
                .Select(unit => $"{Path.GetFileName(unit)}={File.ReadAllText(unit)}")
                .Order(StringComparer.Ordinal),
        ],
        _ => [os.Crontab ?? ""],
    };

    private static IReadOnlyList<string> ExpectedNames(ForgePromotePlatform platform, string name) => platform switch
    {
        ForgePromotePlatform.Windows => [$"Orkeon {name}"],
        ForgePromotePlatform.Linux => [$"orkeon-{name}.timer", $"orkeon-{name}.service"],
        _ => [$"orkeon:{name}"],
    };

    /// <summary>Whether the OS holds <paramref name="name"/>'s registration, running <paramref name="team"/>'s launcher.</summary>
    private bool IsRegistered(FakeScheduleOs os, ForgePromotePlatform platform, string name, string team) => platform switch
    {
        ForgePromotePlatform.Windows =>
            os.Tasks.TryGetValue($"Orkeon {name}", out var xml)
            && xml.Contains($"<Command>{Path.Combine(team, "run.cmd")}</Command>", StringComparison.Ordinal),
        ForgePromotePlatform.Linux =>
            os.EnabledUnits.Contains($"orkeon-{name}.timer")
            && File.ReadAllText(Path.Combine(UnitDirectory, $"orkeon-{name}.service"))
                .Contains($"ExecStart=\"{Path.Combine(team, "run.sh")}\"", StringComparison.Ordinal),
        _ =>
            (os.Crontab ?? "").Split('\n').Any(line =>
                CronScheduleAdapter.IsTagged(line, $"orkeon:{name}")
                && line.Contains($"\"{Path.Combine(team, "run.sh")}\"", StringComparison.Ordinal)),
    };

    private static int RegistrationCount(FakeScheduleOs os, ForgePromotePlatform platform) => platform switch
    {
        ForgePromotePlatform.Windows => os.Tasks.Count,
        ForgePromotePlatform.Linux => os.EnabledUnits.Count,
        _ => (os.Crontab ?? "").Split('\n').Count(line => line.Contains("# orkeon:", StringComparison.Ordinal)),
    };

    /// <summary>The first line of the card in <paramref name="team"/>.</summary>
    private static string CardTitle(string team) =>
        File.ReadAllText(Path.Combine(team, ForgePromoter.CardFileName)).ReplaceLineEndings("\n").Split('\n')[0];

    private static string? Kind(JsonElement e) => e.GetProperty("kind").GetString();

    private static List<string?> Kinds(IEnumerable<JsonElement> events) => [.. events.Select(Kind)];

    private static List<JsonElement> Events(string stdout) => stdout
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonElement.Parse(line))
        .ToList();
}
