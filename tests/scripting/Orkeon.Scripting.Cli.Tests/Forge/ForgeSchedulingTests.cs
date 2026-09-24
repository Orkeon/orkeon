using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The managed schedule (STUDIO-27): <c>forge schedule</c>, <c>forge schedule --check</c> and
/// <c>forge unschedule</c> over each of the three families — the Windows Task Scheduler, the
/// systemd user manager, the crontab — through a hand-written double of the operating system.
/// Nothing here ever runs schtasks, systemctl or crontab.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeSchedulingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-schedule-" + Guid.NewGuid().ToString("N"));

    /// <summary>A team folder the way Studio names one.</summary>
    private string Team => Path.Combine(_workspace, "teams", "ma-veille");

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

    // ── installing ──

    /// <summary>
    /// D-01/D-02/D-03: installing registers the team under the name its folder gives it — the
    /// task « Orkeon ma-veille », the timer orkeon-ma-veille.timer, the cron line marked
    /// « # orkeon:ma-veille » — running its own launcher, and forge.json records what was
    /// installed: expression, family, names, folder, date.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task Installing_registers_the_team_under_its_folder_name_and_records_it(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);

        var (exitCode, events) = await RunAsync(host, "schedule", Team);

        Assert.Equal(0, exitCode);
        var state = Assert.Single(events);
        Assert.Equal("schedule.state", Kind(state));
        Assert.Equal("installed", state.GetProperty("state").GetString());
        Assert.Equal("daily@07:30", state.GetProperty("expression").GetString());
        Assert.Equal(family, state.GetProperty("family").GetString());
        Assert.Equal(Team, state.GetProperty("path").GetString());
        Assert.Equal(ExpectedNames(platform, "ma-veille"), state.GetProperty("names").EnumerateArray().Select(n => n.GetString()));
        Assert.True(IsRegistered(os, platform, "ma-veille", Team));

        var installed = ForgeTeamRecord.TryRead(Team)!.Schedule!.Installed!;
        Assert.Equal("daily@07:30", installed.Expression);
        Assert.Equal(family, installed.Family);
        Assert.Equal(ExpectedNames(platform, "ma-veille"), installed.Names);
        Assert.Equal(Team, installed.Path);
        Assert.Equal("2026-09-24T10:00:00Z", installed.InstalledAt);
    }

    /// <summary>D-01: installing again is reinstalling — one registration, and the check says it is installed.</summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task Installing_twice_leaves_one_registration_and_the_check_says_installed(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);

        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        var (exitCode, events) = await RunAsync(host, "schedule", Team, "--check");

        Assert.Equal(0, exitCode);
        Assert.Equal("installed", Assert.Single(events).GetProperty("state").GetString());
        Assert.Equal(1, RegistrationCount(os, platform));
    }

    /// <summary>
    /// D-01: removing unregisters, deletes schedule/ and the forge.json block; removing again finds
    /// nothing and still succeeds — nothing to remove is a success.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task Removing_unregisters_and_cleans_the_folder_and_removing_again_is_a_success(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);

        var (exitCode, events) = await RunAsync(host, "unschedule", Team);

        Assert.Equal(0, exitCode);
        var state = Assert.Single(events);
        Assert.Equal("absent", state.GetProperty("state").GetString());
        Assert.True(state.GetProperty("removed").GetBoolean());
        Assert.Equal(0, RegistrationCount(os, platform));
        Assert.False(Directory.Exists(Path.Combine(Team, ForgePromoter.ScheduleDirectoryName)));
        Assert.Null(ForgeTeamRecord.TryRead(Team)!.Schedule);
        Assert.NotNull(ForgeTeamRecord.TryRead(Team)!.SessionId);

        var (again, second) = await RunAsync(host, "unschedule", Team);
        Assert.Equal(0, again);
        Assert.False(Assert.Single(second).GetProperty("removed").GetBoolean());
    }

    /// <summary>A folder that never installed its schedule checks absent, and says why.</summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_schedule_never_installed_checks_absent(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);

        var (exitCode, events) = await RunAsync(host, "schedule", Team, "--check");

        Assert.Equal(0, exitCode);
        var state = Assert.Single(events);
        Assert.Equal("absent", state.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.NotInstalled, state.GetProperty("reason").GetString());
        // No record, no name to ask about: the check never guesses one (D-03).
        Assert.Empty(os.Invocations);
    }

    /// <summary>
    /// D-04: the OS refuses — a policy, no user session, a crontab it will not write — and the verb
    /// fails with a typed error that carries the command a person can run instead; nothing is recorded.
    /// </summary>
    [Theory]
    [InlineData("windows", "schtasks /Create", "ERROR: Access is denied.")]
    [InlineData("linux", "systemctl daemon-reload", "Failed to connect to bus: No medium found")]
    [InlineData("other", "crontab -", "crontab: you are not allowed to use this program")]
    public async Task An_os_refusal_is_a_typed_error_with_the_manual_command(string family, string refused, string message)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        os.Refusals[refused] = message;

        var (exitCode, events) = await RunAsync(host, "schedule", Team);

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal("error", Kind(error));
        Assert.Equal(ForgeErrorCodes.ScheduleRefused, error.GetProperty("code").GetString());
        Assert.Contains(message, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(ForgeScheduleAdapters.ManualInstallCommand(platform, Team, "ma-veille"), error.GetProperty("command").GetString());
        Assert.Null(ForgeTeamRecord.TryRead(Team)!.Schedule!.Installed);
        Assert.Equal(0, RegistrationCount(os, platform));
    }

    /// <summary>D-04: a scheduler that is not installed at all — no crontab, no systemctl — is a refusal like any other.</summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_missing_scheduler_is_a_refusal_with_the_manual_command(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        os.MissingPrograms.UnionWith([WindowsTaskScheduleAdapter.Program, SystemdUserScheduleAdapter.Program, CronScheduleAdapter.Program]);

        var (exitCode, events) = await RunAsync(host, "schedule", Team);

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal(ForgeErrorCodes.ScheduleRefused, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("command").GetString()));
    }

    /// <summary>
    /// D-04: every command is a binary and a list of arguments — schtasks, systemctl, crontab —
    /// never a shell, and never a command line assembled from the folder's path.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task No_command_goes_through_a_shell(string family)
    {
        var platform = Platform(family);
        // A folder name a shell would split and interpret, spaces, quote and ampersand included.
        var team = Path.Combine(_workspace, "teams", "l'équipe & co");
        PromoteScheduled(team, platform);
        var (os, host) = Machine(platform);

        Assert.Equal(0, (await RunAsync(host, "schedule", team)).ExitCode);
        Assert.Equal(0, (await RunAsync(host, "schedule", team, "--check")).ExitCode);
        Assert.Equal(0, (await RunAsync(host, "unschedule", team)).ExitCode);

        Assert.NotEmpty(os.Invocations);
        Assert.All(os.Invocations, invocation =>
        {
            Assert.Contains(invocation.FileName, new[] { WindowsTaskScheduleAdapter.Program, SystemdUserScheduleAdapter.Program, CronScheduleAdapter.Program });
            Assert.DoesNotContain(invocation.Arguments, argument => argument is "-c" or "/c" or "/C");
            Assert.DoesNotContain(invocation.Arguments, argument => argument.Contains("&&", StringComparison.Ordinal) || argument.Contains('|'));
        });
    }

    // ── the families' own details ──

    /// <summary>
    /// D-02, cron: the team's line is marked « # orkeon:ma-veille »; the user's own lines are read
    /// and written back exactly as they were, around it and after it is removed.
    /// </summary>
    [Fact]
    public async Task The_cron_line_carries_its_marker_and_the_users_own_lines_stay_as_they_were()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Other);
        var (os, host) = Machine(ForgePromotePlatform.Other);
        const string mine = "MAILTO=me@example.org\n15 3 * * 1 /usr/local/bin/backup # orkeon:ma-veille-copy\n";
        os.Crontab = mine;

        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);

        var lines = os.Crontab!.TrimEnd('\n').Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("MAILTO=me@example.org", lines[0]);
        Assert.Equal("15 3 * * 1 /usr/local/bin/backup # orkeon:ma-veille-copy", lines[1]);
        Assert.Equal($"30 7 * * * \"{Path.Combine(Team, "run.sh")}\" # orkeon:ma-veille", lines[2]);

        Assert.Equal(0, (await RunAsync(host, "unschedule", Team)).ExitCode);
        Assert.Equal(mine, os.Crontab);
        Assert.All(os.Invocations, invocation => Assert.Equal(CronScheduleAdapter.Program, invocation.FileName));
        Assert.Contains(os.Invocations, invocation => invocation.Arguments is ["-"] && invocation.StandardInput is not null);
    }

    /// <summary>A crontab that cannot be read is never written over: the user's own lines would be lost.</summary>
    [Fact]
    public async Task A_crontab_that_cannot_be_read_is_never_written_over()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Other);
        var (os, host) = Machine(ForgePromotePlatform.Other);
        os.Refusals["crontab -l"] = "crontab: cannot open your crontab: Permission denied";

        var (exitCode, events) = await RunAsync(host, "schedule", Team);

        Assert.Equal(1, exitCode);
        Assert.Equal(ForgeErrorCodes.ScheduleRefused, Assert.Single(events).GetProperty("code").GetString());
        Assert.DoesNotContain(os.Invocations, invocation => invocation.Arguments is ["-"]);
    }

    /// <summary>D-02, systemd: the units are copied into the user's unit directory, and an install the manager refuses leaves none behind.</summary>
    [Fact]
    public async Task The_units_land_in_the_user_directory_and_a_refused_install_leaves_none()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Linux);
        var (os, host) = Machine(ForgePromotePlatform.Linux);

        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        Assert.Contains($"ExecStart=\"{Path.Combine(Team, "run.sh")}\"",
            await File.ReadAllTextAsync(Path.Combine(UnitDirectory, "orkeon-ma-veille.service"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Equal(
            ["--user daemon-reload", "--user enable --now orkeon-ma-veille.timer"],
            os.Invocations.Where(i => i.Arguments[1] is "daemon-reload" or "enable").Select(i => string.Join(' ', i.Arguments)));

        Assert.Equal(0, (await RunAsync(host, "unschedule", Team)).ExitCode);
        Assert.False(File.Exists(Path.Combine(UnitDirectory, "orkeon-ma-veille.timer")));
        Assert.Empty(os.EnabledUnits);

        var evening = Path.Combine(_workspace, "teams", "veille-soir");
        PromoteScheduled(evening, ForgePromotePlatform.Linux);
        os.Refusals["systemctl enable"] = "Failed to connect to bus: No medium found";
        Assert.Equal(1, (await RunAsync(host, "schedule", evening)).ExitCode);
        Assert.Empty(Directory.GetFiles(UnitDirectory));
    }

    /// <summary>
    /// A service left without its timer — a unit deleted by hand — never fires: the check says to
    /// reinstall, and the removal deletes what is left without asking systemd to disable a timer
    /// that is not there, so the team stays deletable.
    /// </summary>
    [Fact]
    public async Task A_service_left_without_its_timer_is_stale_and_removable()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Linux);
        var (os, host) = Machine(ForgePromotePlatform.Linux);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        File.Delete(Path.Combine(UnitDirectory, "orkeon-ma-veille.timer"));
        os.EnabledUnits.Clear();

        var check = Assert.Single((await RunAsync(host, "schedule", Team, "--check")).Events);
        Assert.Equal("stale", check.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Disabled, check.GetProperty("reason").GetString());

        os.Invocations.Clear();
        Assert.Equal(0, (await RunAsync(host, "unschedule", Team)).ExitCode);
        Assert.False(File.Exists(Path.Combine(UnitDirectory, "orkeon-ma-veille.service")));
        Assert.DoesNotContain(os.Invocations, invocation => invocation.Arguments.Contains("disable"));
    }

    /// <summary>D-02, Windows: the task is created from schedule/windows-task.xml with /F, and read back with /Query /XML.</summary>
    [Fact]
    public async Task The_task_is_created_from_its_xml_and_read_back_to_compare_what_it_runs()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Windows);
        var (os, host) = Machine(ForgePromotePlatform.Windows);

        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        Assert.Contains(os.Invocations, i => i.Arguments.SequenceEqual(
            ["/Create", "/TN", "Orkeon ma-veille", "/XML", Path.Combine(Team, "schedule", "windows-task.xml"), "/F"]));
        Assert.Contains($"<Command>{Path.Combine(Team, "run.cmd")}</Command>", os.Tasks["Orkeon ma-veille"], StringComparison.Ordinal);

        os.Invocations.Clear();
        Assert.Equal(0, (await RunAsync(host, "schedule", Team, "--check")).ExitCode);
        Assert.Equal(["/Query", "/TN", "Orkeon ma-veille", "/XML"], Assert.Single(os.Invocations).Arguments);

        // A laptop on battery at the chosen time still runs its team, and a run missed while the
        // machine was off happens when it next can — the systemd timer's Persistent=true.
        var task = os.Tasks["Orkeon ma-veille"];
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", task, StringComparison.Ordinal);
        Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", task, StringComparison.Ordinal);
        Assert.Contains("<StartWhenAvailable>true</StartWhenAvailable>", task, StringComparison.Ordinal);
        Assert.DoesNotContain("<Principal", task, StringComparison.Ordinal);
    }

    /// <summary>
    /// DA-3: the card no longer asks a person to install the schedule — Studio installs it with the
    /// user's consent, or <c>forge schedule</c> does; the command by hand stays, as the fallback.
    /// </summary>
    [Fact]
    public void The_card_says_who_installs_the_schedule_and_keeps_the_manual_command()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Windows);

        var card = File.ReadAllText(Path.Combine(Team, ForgePromoter.CardFileName));

        Assert.Contains("`orkeon forge schedule .`", card, StringComparison.Ordinal);
        Assert.Contains("`orkeon forge unschedule .`", card, StringComparison.Ordinal);
        Assert.Contains("Orkeon Studio installe la planification avec votre accord", card, StringComparison.Ordinal);
        Assert.DoesNotContain("installez l'artefact vous-même", card, StringComparison.Ordinal);
        Assert.Contains(ForgeScheduleAdapters.ManualInstallCommand(ForgePromotePlatform.Windows, Team, "ma-veille"), card, StringComparison.Ordinal);
    }

    /// <summary>Without <c>--events</c> the verb speaks to a person: one line, in words.</summary>
    [Fact]
    public async Task Without_events_the_verb_says_it_in_words()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Other);
        var (_, host) = Machine(ForgePromotePlatform.Other);
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["schedule", Team], _workspace, host));

        Assert.Equal("✔ scheduled (daily@07:30): orkeon:ma-veille", console.Stdout.Trim());
    }

    /// <summary>A registration disabled by hand still exists, and no longer fires: to reinstall.</summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_registration_disabled_by_hand_checks_stale(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        switch (platform)
        {
            case ForgePromotePlatform.Windows:
                os.DisabledTasks.Add("Orkeon ma-veille");
                break;
            case ForgePromotePlatform.Linux:
                os.EnabledUnits.Clear();
                break;
            default:
                os.Crontab = "#" + os.Crontab;
                break;
        }

        var state = Assert.Single((await RunAsync(host, "schedule", Team, "--check")).Events);

        Assert.Equal("stale", state.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Disabled, state.GetProperty("reason").GetString());
    }

    // ── a folder that moved, a copy, a name taken ──

    /// <summary>
    /// The folder was renamed by hand after its schedule was installed: the registration runs a
    /// path that is gone, under the former name. The check says it is stale; installing removes
    /// that registration — recorded, never guessed — and installs the folder under its new name.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_moved_folder_checks_stale_and_reinstalls_under_its_new_name(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        var moved = Path.Combine(_workspace, "teams", "veille-matin");
        Directory.Move(Team, moved);

        var check = Assert.Single((await RunAsync(host, "schedule", moved, "--check")).Events);
        Assert.Equal("stale", check.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Moved, check.GetProperty("reason").GetString());

        Assert.Equal(0, (await RunAsync(host, "schedule", moved)).ExitCode);

        Assert.False(IsRegistered(os, platform, "ma-veille", Team));
        Assert.True(IsRegistered(os, platform, "veille-matin", moved));
        Assert.Equal(1, RegistrationCount(os, platform));
        Assert.Equal(moved, ForgeTeamRecord.TryRead(moved)!.Schedule!.Installed!.Path);
        Assert.Equal("installed", Assert.Single((await RunAsync(host, "schedule", moved, "--check")).Events).GetProperty("state").GetString());
    }

    /// <summary>
    /// A copy of a scheduled team carries its original's forge.json, installed block included. The
    /// registration it names is the original's, which still claims it: the copy checks absent,
    /// its removal leaves the original's in place, and installing it registers one of its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_copy_never_touches_its_originals_registration(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        var copy = Path.Combine(_workspace, "teams", "ma-veille-copy");
        ForgePromoter.CopyDirectory(Team, copy);

        var check = Assert.Single((await RunAsync(host, "schedule", copy, "--check")).Events);
        Assert.Equal("absent", check.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Copy, check.GetProperty("reason").GetString());

        var (removal, removalEvents) = await RunAsync(host, "unschedule", copy);
        Assert.Equal(0, removal);
        Assert.Equal(ForgeErrorCodes.ScheduleNotOwned, removalEvents[0].GetProperty("code").GetString());
        Assert.False(removalEvents[^1].GetProperty("removed").GetBoolean());
        Assert.True(IsRegistered(os, platform, "ma-veille", Team));

        ForgePromoter.CopyDirectory(Team, Path.Combine(_workspace, "teams", "ma-veille-copy-2"));
        var second = Path.Combine(_workspace, "teams", "ma-veille-copy-2");
        Assert.Equal(0, (await RunAsync(host, "schedule", second)).ExitCode);
        Assert.True(IsRegistered(os, platform, "ma-veille", Team));
        Assert.True(IsRegistered(os, platform, "ma-veille-copy-2", second));
        Assert.Equal(2, RegistrationCount(os, platform));
    }

    /// <summary>
    /// Two team folders of the same name in two places take the same scheduler name: the second
    /// is refused, and the first team's registration is never replaced.
    /// </summary>
    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_name_another_team_holds_is_never_replaced(string family)
    {
        var platform = Platform(family);
        PromoteScheduled(Team, platform);
        var elsewhere = Path.Combine(_workspace, "elsewhere", "ma-veille");
        PromoteScheduled(elsewhere, platform);
        var (os, host) = Machine(platform);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);

        var (exitCode, events) = await RunAsync(host, "schedule", elsewhere);

        Assert.Equal(1, exitCode);
        var error = Assert.Single(events);
        Assert.Equal(ForgeErrorCodes.ScheduleNameTaken, error.GetProperty("code").GetString());
        Assert.Contains(Team, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(IsRegistered(os, platform, "ma-veille", Team));
    }

    // ── the promotion's part ──

    /// <summary>D-03: the promotion records the declared schedule in forge.json; a promotion without one records none.</summary>
    [Fact]
    public void The_promotion_records_the_declared_schedule_and_nothing_when_there_is_none()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Linux, "hourly");
        Assert.Equal("hourly", ForgeTeamRecord.TryRead(Team)!.Schedule!.Expression);
        Assert.Null(ForgeTeamRecord.TryRead(Team)!.Schedule!.Installed);

        var bare = Path.Combine(_workspace, "teams", "sans-planification");
        ForgePromoter.Promote(ReadySession(), bare, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);
        Assert.Null(ForgeTeamRecord.TryRead(bare)!.Schedule);
    }

    /// <summary>
    /// A re-adoption rewrites forge.json and keeps what was installed — the names are the only way
    /// back to the registration. One that drops the schedule touches no OS: it warns that the
    /// registration still runs the team, and the check calls it stale until it is removed.
    /// </summary>
    [Fact]
    public async Task A_readoption_keeps_the_installed_block_and_one_that_drops_the_schedule_warns()
    {
        var session = PromoteScheduled(Team, ForgePromotePlatform.Linux);
        var (os, host) = Machine(ForgePromotePlatform.Linux);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        session.Document.PromotedTo = Team;
        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(Now);
        var invocations = os.Invocations.Count;

        var (exitCode, events) = await RunAsync(host, "promote", session.Document.Slug, "--to", Team);

        Assert.Equal(0, exitCode);
        Assert.Equal(invocations, os.Invocations.Count);
        var warning = Assert.Single(events, e => Kind(e) == "warning");
        Assert.Equal(ForgeErrorCodes.ScheduleStillInstalled, warning.GetProperty("code").GetString());
        Assert.Contains("forge unschedule", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
        var record = ForgeTeamRecord.TryRead(Team)!;
        Assert.Null(record.Schedule!.Expression);
        Assert.NotNull(record.Schedule.Installed);

        var check = Assert.Single((await RunAsync(host, "schedule", Team, "--check")).Events);
        Assert.Equal("stale", check.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Undeclared, check.GetProperty("reason").GetString());

        Assert.Equal(0, (await RunAsync(host, "unschedule", Team)).ExitCode);
        Assert.Empty(os.EnabledUnits);
    }

    /// <summary>A re-adoption that changes the schedule leaves the registration of the former one: stale until reinstalled.</summary>
    [Fact]
    public async Task A_readoption_that_changes_the_schedule_checks_stale_until_reinstalled()
    {
        var session = PromoteScheduled(Team, ForgePromotePlatform.Windows);
        var (_, host) = Machine(ForgePromotePlatform.Windows);
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        session.Document.PromotedTo = Team;
        Assert.True(ForgeSchedule.TryParse("hourly", out var hourly, out _));

        ForgePromoter.Promote(session, Team, hourly, null, false, ForgePromotePlatform.Windows, Now);

        var check = Assert.Single((await RunAsync(host, "schedule", Team, "--check")).Events);
        Assert.Equal("stale", check.GetProperty("state").GetString());
        Assert.Equal(ForgeScheduleReasons.Changed, check.GetProperty("reason").GetString());
        Assert.Equal(0, (await RunAsync(host, "schedule", Team)).ExitCode);
        Assert.Equal("installed", Assert.Single((await RunAsync(host, "schedule", Team, "--check")).Events).GetProperty("state").GetString());
    }

    /// <summary>A folder that declares no schedule has nothing to install: said, with the code.</summary>
    [Fact]
    public async Task A_folder_that_declares_no_schedule_cannot_be_scheduled()
    {
        ForgePromoter.Promote(ReadySession(), Team, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);
        var (os, host) = Machine(ForgePromotePlatform.Linux);

        var (exitCode, events) = await RunAsync(host, "schedule", Team);

        Assert.Equal(1, exitCode);
        Assert.Equal(ForgeErrorCodes.ScheduleNone, Assert.Single(events).GetProperty("code").GetString());
        Assert.Empty(os.Invocations);

        var (missing, missingEvents) = await RunAsync(host, "unschedule", Path.Combine(_workspace, "nowhere"));
        Assert.Equal(1, missing);
        Assert.Equal(ForgeErrorCodes.TeamUnreadable, Assert.Single(missingEvents).GetProperty("code").GetString());
    }

    /// <summary>
    /// The schedule/ of a copied folder names its original's launcher and name: installing
    /// regenerates it for the folder as it is now, and never registers another folder's launcher.
    /// </summary>
    [Fact]
    public async Task Installing_regenerates_artifacts_that_describe_another_folder()
    {
        PromoteScheduled(Team, ForgePromotePlatform.Linux);
        var copy = Path.Combine(_workspace, "teams", "ma-veille-copy");
        ForgePromoter.CopyDirectory(Team, copy);
        var (os, host) = Machine(ForgePromotePlatform.Linux);

        Assert.Equal(0, (await RunAsync(host, "schedule", copy)).ExitCode);

        var schedule = Path.Combine(copy, ForgePromoter.ScheduleDirectoryName);
        Assert.False(File.Exists(Path.Combine(schedule, "orkeon-ma-veille.service")));
        Assert.Contains($"ExecStart=\"{Path.Combine(copy, "run.sh")}\"",
            await File.ReadAllTextAsync(Path.Combine(schedule, "orkeon-ma-veille-copy.service"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.Contains("orkeon-ma-veille-copy.timer", os.EnabledUnits);
    }

    // ── the grammar ──

    /// <summary>The two verbs take the team folder, and nothing but --events — and --check for schedule.</summary>
    [Fact]
    public void Schedule_and_unschedule_take_the_team_folder_and_only_events_and_check()
    {
        var schedule = ForgeCommandOptions.Parse(["schedule", "/teams/veille", "--check", "--events", "jsonl"]);
        Assert.Null(schedule.Error);
        Assert.Equal("/teams/veille", schedule.ScheduleDirectory);
        Assert.True(schedule.Check);
        Assert.True(schedule.Events);

        var unschedule = ForgeCommandOptions.Parse(["unschedule", "/teams/veille"]);
        Assert.Null(unschedule.Error);
        Assert.Equal("/teams/veille", unschedule.UnscheduleDirectory);

        Assert.Contains("needs the team folder", ForgeCommandOptions.Parse(["schedule"]).Error, StringComparison.Ordinal);
        Assert.Contains("needs the team folder", ForgeCommandOptions.Parse(["schedule", "--check"]).Error, StringComparison.Ordinal);
        Assert.Contains("needs the team folder", ForgeCommandOptions.Parse(["unschedule"]).Error, StringComparison.Ordinal);
        Assert.Contains("--check only applies to `forge schedule`", ForgeCommandOptions.Parse(["unschedule", "/t", "--check"]).Error, StringComparison.Ordinal);
        Assert.Contains("--check only applies to `forge schedule`", ForgeCommandOptions.Parse(["une veille", "--check"]).Error, StringComparison.Ordinal);
        Assert.Contains("take no option but --events", ForgeCommandOptions.Parse(["schedule", "/t", "--dry"]).Error, StringComparison.Ordinal);
        Assert.Contains("take no option but --events", ForgeCommandOptions.Parse(["unschedule", "/t", "de trop"]).Error, StringComparison.Ordinal);
        Assert.Contains("only apply to `forge promote`", ForgeCommandOptions.Parse(["schedule", "/t", "--schedule", "hourly"]).Error, StringComparison.Ordinal);
        Assert.Contains("--read only applies", ForgeCommandOptions.Parse(["schedule", "/t", "--read", "/d"]).Error, StringComparison.Ordinal);
        // Only the first argument is a verb.
        Assert.Equal("veille schedule", ForgeCommandOptions.Parse(["veille", "schedule"]).Need);
    }

    // ── helpers ──

    private ForgeSession ReadySession(string slug = "veille")
    {
        var session = ForgeSession.Create(_workspace, slug);
        session.Document.Title = "Résumer chaque matin les nouvelles offres";
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

    /// <summary>A team promoted into <paramref name="destination"/> with <paramref name="schedule"/>, as <paramref name="platform"/> would.</summary>
    private ForgeSession PromoteScheduled(string destination, ForgePromotePlatform platform, string schedule = "daily@07:30")
    {
        Assert.True(ForgeSchedule.TryParse(schedule, out var parsed, out _));
        var session = ReadySession();
        ForgePromoter.Promote(session, destination, parsed, settingsPath: null, copySettings: false, platform, Now);
        return session;
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

    private static string? Kind(JsonElement e) => e.GetProperty("kind").GetString();

    private static List<JsonElement> Events(string stdout) => stdout
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonElement.Parse(line))
        .ToList();
}
