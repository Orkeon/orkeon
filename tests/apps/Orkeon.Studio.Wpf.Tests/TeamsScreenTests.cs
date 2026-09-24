using System.Globalization;
using System.Windows.Input;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-32, the « My teams » screen that stays readable with many teams: a search and an order
/// applied to the cards in memory (D-01), « Archive » without a question and an undo banner on a
/// timer of its own (D-02), a sidebar count of the active teams whatever the screen shows (D-03),
/// three empty states told apart (D-04), and the suggestion to archive the teams nobody launches
/// any more (DB-1). Then the three points STUDIO-31 handed over: an archive made behind the
/// launchers' back is read again at launch, a copy's arrival is activity, and undoing « Stop the
/// schedule and archive » says the schedule stays stopped.
/// </summary>
public sealed class TeamsScreenTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-screen-{Guid.NewGuid():N}");

    private string TeamsRoot => Path.Combine(_root, "teams");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Text(string key) => EnglishStudioStrings.Instance[key];

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), arguments);

    private static ProcessOutputLine Out(string json) => ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    /// <summary>A team folder with its sidecar; its last run from Studio <paramref name="ranDaysAgo"/> days ago when given.</summary>
    private string Team(
        string slug,
        string? name = null,
        string? description = null,
        int? ranDaysAgo = null,
        string? schedule = null,
        bool archived = false)
    {
        var team = Path.Combine(TeamsRoot, slug);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = name ?? slug,
            Description = description,
            Schedule = schedule,
            LastRunAt = ranDaysAgo is { } days ? Now.AddDays(-days) : null,
        });
        if (archived)
            Assert.True(TeamCatalog.Archive(team, Now.AddDays(-1)));
        return team;
    }

    private TeamsViewModel Screen(
        Func<StudioSettings>? settings = null,
        IUiDelay? undoDelay = null,
        ILaunchHistoryStore? history = null,
        Func<string, TeamActivity>? activityOf = null,
        FakeProcessLauncher? processes = null,
        Func<IReadOnlyList<TeamSummary>>? loadTeams = null,
        IShellOpener? shellOpener = null) =>
        new(new TeamsDependencies
        {
            TeamsRoot = TeamsRoot,
            WorkspaceDirectory = _root,
            LoadSessions = () => [],
            LoadTeams = loadTeams,
            HistoryStore = history,
            ShellOpener = shellOpener,
            Clock = new StubTimeProvider { Now = Now },
            StudioSettings = settings,
            UndoDelay = undoDelay,
            ActivityOf = activityOf,
            Forge = processes is null
                ? null
                : new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
        });

    private static IEnumerable<string> Slugs(IEnumerable<TeamCardViewModel> cards) => cards.Select(card => card.Slug);

    // ── D-01: the search and the order, in memory ──

    /// <summary>
    /// The search reads the name and the description — every word typed starts a word of them,
    /// accents and case aside, the gallery's own rule — and never the disk: a refresh resets the
    /// list, and the shell re-reads the settings' team folders and the mount references on it.
    /// </summary>
    [Fact]
    public void The_search_filters_names_and_descriptions_in_memory_without_reading_the_disk_again()
    {
        Team("veille", "Veille concurrentielle", "Relit la presse du secteur chaque matin.");
        Team("rapport", "Rapport hebdomadaire", "Assemble la note de la semaine.");
        Team("factures", "Tri des factures", "Classe les factures par fournisseur.");
        Team("synthese", "Synthèse des réunions", "Résume les comptes rendus.");
        var reads = 0;
        var screen = Screen(loadTeams: () =>
        {
            reads++;
            return TeamCatalog.List(TeamsRoot, TeamListFilter.All);
        });
        var resets = 0;
        screen.Teams.CollectionChanged += (_, _) => resets++;
        reads = 0;

        screen.SearchText = "rap";
        Assert.Equal(["rapport"], Slugs(screen.Cards));

        screen.SearchText = "PRESSE";
        Assert.Equal(["veille"], Slugs(screen.Cards));

        screen.SearchText = "reunion";
        Assert.Equal(["synthese"], Slugs(screen.Cards));

        screen.SearchText = "tri four";
        Assert.Equal(["factures"], Slugs(screen.Cards));

        screen.ClearSearchCommand.Execute(null);
        Assert.Equal("", screen.SearchText);
        Assert.Equal(4, screen.Cards.Count);

        Assert.Equal(0, reads);
        Assert.Equal(0, resets);
    }

    /// <summary>
    /// The order is the last activity by default (STUDIO-31, D-05) — the most recent of the last run
    /// the sidecar records, the history, the promotion and a copy's arrival — a team whose activity is
    /// unknown last; or the name. A copy of an old team is a fresh team: it comes first.
    /// </summary>
    [Fact]
    public async Task The_cards_come_most_recent_activity_first_and_by_name_on_demand()
    {
        Team("ancienne", "Zeta ancienne", ranDaysAgo: 30);
        var fromHistory = Team("historique", "Alpha historique", ranDaysAgo: 50);
        var promoted = Team("promue", "Gamma promue");
        await File.WriteAllTextAsync(
            Path.Combine(promoted, ForgeSessionCatalog.TeamRecordFileName),
            """{"v":1,"id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","promotedAt":"2026-09-14T10:00:00Z"}""",
            TestContext.Current.CancellationToken);
        Team("muette", "Beta muette");
        Assert.NotNull(TeamCatalog.Duplicate(Team("modele", "Delta modele", ranDaysAgo: 300), Now.AddHours(-1)));
        var history = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(new LaunchHistoryEntry { Target = fromHistory, StartedAt = Now.AddDays(-2) }),
        };
        var screen = Screen(history: history);
        await screen.LoadLastRunsAsync(TestContext.Current.CancellationToken);

        Assert.True(screen.IsSortedByActivity);
        Assert.Equal(["modele-copy", "historique", "promue", "ancienne", "modele", "muette"], Slugs(screen.Cards));

        screen.SortByNameCommand.Execute(null);

        Assert.True(screen.IsSortedByName);
        Assert.Equal(["historique", "muette", "modele", "modele-copy", "promue", "ancienne"], Slugs(screen.Cards));

        // The order is kept across a refresh — the view is the screen's, not the catalogue's.
        screen.Refresh();
        Assert.Equal(TeamSortOrder.Name, screen.SortOrder);
        Assert.Equal("historique", screen.Cards[0].Slug);

        screen.SortByActivityCommand.Execute(null);
        Assert.Equal("modele-copy", screen.Cards[0].Slug);
    }

    // ── D-02: « Archive », then an undo banner ──

    /// <summary>
    /// No question before the archive — it is undone in one click — and the banner that offers the
    /// click runs on a timer of its own: dropping it must never drop the assistant's beats.
    /// </summary>
    [Fact]
    public async Task Archiving_offers_an_undo_on_its_own_timer_that_puts_the_card_back()
    {
        var veille = Team("veille", "Veille", ranDaysAgo: 1);
        Team("rapport", "Rapport", ranDaysAgo: 2);
        var undoDelay = new ManualUiDelay();
        var screen = Screen(undoDelay: undoDelay);
        var changed = new List<string>();
        screen.ArchiveChanged += (_, e) => changed.AddRange(e.Paths);

        await screen.Cards.Single(card => card.Slug == "veille").ArchiveCommand.ExecuteAsync();

        Assert.True(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(["rapport"], Slugs(screen.Cards));
        Assert.True(screen.HasUndo);
        Assert.Equal(Format(StudioStringKeys.TeamsArchivedUndo, "Veille"), screen.UndoNotice);
        Assert.Equal(1, undoDelay.Pending);
        Assert.InRange(Assert.Single(undoDelay.Requested), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15));

        screen.UndoCommand.Execute(null);

        Assert.False(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(["veille", "rapport"], Slugs(screen.Cards));
        Assert.False(screen.HasUndo);
        Assert.Equal(0, undoDelay.Pending);
        Assert.Equal([veille, veille], changed);
    }

    /// <summary>The banner goes by itself, and the archive stands; a second archive's banner replaces the first's.</summary>
    [Fact]
    public async Task The_undo_banner_goes_by_itself_and_only_the_latest_archive_is_offered()
    {
        var veille = Team("veille", "Veille", ranDaysAgo: 1);
        var rapport = Team("rapport", "Rapport", ranDaysAgo: 2);
        var undoDelay = new ManualUiDelay();
        var screen = Screen(undoDelay: undoDelay);

        await screen.Cards.Single(card => card.Slug == "veille").ArchiveCommand.ExecuteAsync();
        await screen.Cards.Single(card => card.Slug == "rapport").ArchiveCommand.ExecuteAsync();

        Assert.Equal(Format(StudioStringKeys.TeamsArchivedUndo, "Rapport"), screen.UndoNotice);
        Assert.Equal(1, undoDelay.Pending);

        undoDelay.Elapse();

        Assert.False(screen.HasUndo);
        Assert.False(screen.UndoCommand.CanExecute(null));
        Assert.True(TeamCatalog.Describe(veille).IsArchived);
        Assert.True(TeamCatalog.Describe(rapport).IsArchived);
    }

    /// <summary>
    /// Without a timer of its own the banner cannot stay: the immediate delay plays the expiry on the
    /// way in — which is what every screen built without one gets, the shell's tests included.
    /// </summary>
    [Fact]
    public async Task Without_a_timer_the_archive_goes_through_and_no_banner_stays()
    {
        var veille = Team("veille", ranDaysAgo: 1);
        var screen = Screen();

        await Assert.Single(screen.Cards).ArchiveCommand.ExecuteAsync();

        Assert.True(TeamCatalog.Describe(veille).IsArchived);
        Assert.False(screen.HasUndo);
    }

    // ── D-01: the Archives view ──

    [Fact]
    public void The_archives_view_lists_the_archives_only_and_its_toggle_counts_them()
    {
        Team("veille", "Veille", ranDaysAgo: 1);
        Team("ancienne", "Ancienne veille", archived: true);
        Team("salon", "Veille du salon", archived: true);
        var screen = Screen();

        Assert.Equal(["veille"], Slugs(screen.Cards));
        Assert.True(screen.HasArchives);
        Assert.Equal(Format(StudioStringKeys.TeamsArchivesLabel, 2), screen.ArchivesLabel);

        screen.ToggleArchivesCommand.Execute(null);

        Assert.True(screen.ShowArchives);
        Assert.Equal(["ancienne", "salon"], Slugs(screen.Cards));
        Assert.All(screen.Cards, card => Assert.True(card.IsArchived));

        // The search works in the archives too, and follows the user back to the active teams.
        screen.SearchText = "sal";
        Assert.Equal(["salon"], Slugs(screen.Cards));

        screen.ToggleArchivesCommand.Execute(null);

        Assert.False(screen.ShowArchives);
        Assert.Empty(screen.Cards);
        Assert.True(screen.HasNoMatch);
    }

    /// <summary>The Archives view exists while it has something to show: the last restore closes it.</summary>
    [Fact]
    public void Restoring_the_last_archive_brings_the_active_teams_back_on_screen()
    {
        Team("veille", "Veille", ranDaysAgo: 1);
        Team("ancienne", "Ancienne", ranDaysAgo: 3, archived: true);
        var screen = Screen();
        screen.ToggleArchivesCommand.Execute(null);

        Assert.Single(screen.Cards).RestoreCommand.Execute(null);

        Assert.False(screen.ShowArchives);
        Assert.False(screen.HasArchives);
        Assert.Equal(["veille", "ancienne"], Slugs(screen.Cards));

        // With nothing archived there is nothing to open.
        screen.ToggleArchivesCommand.Execute(null);
        Assert.False(screen.ShowArchives);
    }

    /// <summary>
    /// D-02: an archived card offers « Restore » and « Delete », nothing else — no Launch, no Modify,
    /// no Rename, no copy, no folders to change, no schedule to install.
    /// </summary>
    [Fact]
    public void An_archived_card_offers_restore_and_delete_and_nothing_else()
    {
        // Both declare a schedule nothing runs — the state that offers « Install the schedule ».
        foreach (var (slug, archived) in new[] { ("veille", false), ("ancienne", true) })
        {
            var team = Team(slug, schedule: "daily@07:30", archived: archived);
            Directory.CreateDirectory(Path.Combine(team, "crew"));
            File.WriteAllText(Path.Combine(team, "crew", "config.yaml"), $"name: {slug}\n");
        }

        var screen = Screen(processes: new FakeProcessLauncher(), shellOpener: new RecordingShellOpener());
        screen.RecordScheduleState(Path.Combine(TeamsRoot, "veille"), TeamScheduleState.Absent);
        screen.RecordScheduleState(Path.Combine(TeamsRoot, "ancienne"), TeamScheduleState.Absent);
        var active = Assert.Single(screen.Teams);
        var card = Assert.Single(screen.ArchivedTeams);
        ICommand[] ActiveOnly(TeamCardViewModel c) =>
        [
            c.LaunchCommand, c.ModifyCommand, c.RenameCommand, c.ArchiveCommand, c.StopScheduleAndArchiveCommand,
            c.DuplicateCommand, c.ExportCommand, c.ChangeMountsCommand, c.OpenCommand,
        ];

        Assert.All(ActiveOnly(active), command => Assert.True(command.CanExecute(null)));
        Assert.False(active.RestoreCommand.CanExecute(null));
        Assert.True(active.ShowsInstallSchedule);

        Assert.All(ActiveOnly(card), command => Assert.False(command.CanExecute(null)));
        Assert.True(card.RestoreCommand.CanExecute(null));
        Assert.True(card.AskDeleteCommand.CanExecute(null));
        Assert.False(card.ShowsInstallSchedule);
        Assert.Equal(Text(StudioStringKeys.TeamsArchivedBadge), card.BadgeText);
        Assert.Equal("muted", card.BadgeTone);
        Assert.Contains(
            Format(StudioStringKeys.TeamsArchivedOn, Now.AddDays(-1).ToLocalTime().ToString("d", CultureInfo.CurrentCulture)),
            card.MetaLine,
            StringComparison.Ordinal);
    }

    // ── D-03: the sidebar count ──

    [Fact]
    public void The_sidebar_count_is_the_active_teams_whatever_the_search_or_the_view()
    {
        Team("veille", ranDaysAgo: 1);
        Team("rapport", ranDaysAgo: 2);
        Team("ancienne", archived: true);
        var screen = Screen();
        var raised = new List<string?>();
        screen.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal(2, screen.ActiveCount);

        screen.SearchText = "vei";
        screen.SortByNameCommand.Execute(null);
        Assert.Single(screen.Cards);
        screen.ToggleArchivesCommand.Execute(null);
        Assert.Empty(screen.Cards);

        Assert.Equal(2, screen.ActiveCount);
        Assert.Equal(1, screen.ArchivedCount);
        Assert.DoesNotContain(nameof(TeamsViewModel.ActiveCount), raised);
    }

    // ── D-04: three empty states ──

    /// <summary>Everything archived is not an empty screen: the teams are there, and the empty state opens the archives.</summary>
    [Fact]
    public void Every_team_archived_is_not_an_empty_screen_the_empty_state_opens_the_archives()
    {
        Team("ancienne", "Ancienne", archived: true);
        var screen = Screen();

        Assert.False(screen.IsEmpty);
        Assert.True(screen.IsAllArchived);
        Assert.False(screen.HasNoMatch);
        Assert.Empty(screen.Cards);
        Assert.Equal(0, screen.ActiveCount);

        screen.ToggleArchivesCommand.Execute(null);

        Assert.False(screen.IsAllArchived);
        Assert.Equal(["ancienne"], Slugs(screen.Cards));
    }

    [Fact]
    public void No_team_at_all_and_a_search_that_finds_nothing_are_the_two_other_states()
    {
        Directory.CreateDirectory(TeamsRoot);
        var screen = Screen();

        Assert.True(screen.IsEmpty);
        Assert.False(screen.IsAllArchived);
        Assert.False(screen.HasNoMatch);

        Team("veille", "Veille", ranDaysAgo: 1);
        screen.Refresh();
        screen.SearchText = "  zzz ";

        Assert.False(screen.IsEmpty);
        Assert.True(screen.HasNoMatch);
        Assert.Equal(Format(StudioStringKeys.TeamsSearchNoMatch, "zzz"), screen.NoMatchLine);

        screen.ClearSearchCommand.Execute(null);

        Assert.False(screen.HasNoMatch);
        Assert.Equal("", screen.NoMatchLine);
        Assert.Single(screen.Cards);
    }

    // ── DB-1: the archive suggestion ──

    /// <summary>
    /// The teams not launched for sixty days — never a scheduled one, whose runs the system makes
    /// without Studio, nor one whose last activity is unknown, which nothing says is old.
    /// </summary>
    [Fact]
    public void The_suggestion_lists_the_teams_past_the_threshold_never_a_scheduled_one_nor_an_unknown_one()
    {
        Team("ancienne", "Ancienne veille", ranDaysAgo: 90);
        Team("oubliee", "Revue oubliée", ranDaysAgo: 61);
        Team("recente", "Récente", ranDaysAgo: 59);
        Team("planifiee", "Planifiée", ranDaysAgo: 200, schedule: "daily@07:00");
        Team("muette", "Muette");
        var screen = Screen();

        Assert.True(screen.HasArchiveSuggestion);
        Assert.Equal(["ancienne", "oubliee"], Slugs(screen.SuggestedTeams));
        Assert.Equal(Format(StudioStringKeys.TeamsSuggestionMany, 2, 60), screen.ArchiveSuggestionLine);
        Assert.Equal("Ancienne veille, Revue oubliée", screen.ArchiveSuggestionNames);
    }

    /// <summary>One team is named in the sentence itself; the list of names is for several.</summary>
    [Fact]
    public void A_suggestion_of_one_team_names_it()
    {
        Team("ancienne", "Ancienne veille", ranDaysAgo: 90);
        Team("recente", "Récente", ranDaysAgo: 5);
        var screen = Screen();

        Assert.Equal(Format(StudioStringKeys.TeamsSuggestionOne, "Ancienne veille", 60), screen.ArchiveSuggestionLine);
        Assert.Equal("", screen.ArchiveSuggestionNames);
    }

    /// <summary>
    /// Accepting archives exactly the teams listed, and one undo brings them all back. Answered, the
    /// suggestion is not made again this session — least of all right after the undo.
    /// </summary>
    [Fact]
    public void Accepting_the_suggestion_archives_the_listed_teams_and_one_undo_brings_them_all_back()
    {
        var ancienne = Team("ancienne", "Ancienne", ranDaysAgo: 90);
        var oubliee = Team("oubliee", "Oubliée", ranDaysAgo: 61);
        Team("recente", "Récente", ranDaysAgo: 5);
        var undoDelay = new ManualUiDelay();
        var screen = Screen(undoDelay: undoDelay);
        var changed = new List<string>();
        screen.ArchiveChanged += (_, e) => changed.Add(string.Join("|", e.Paths));

        screen.ArchiveSuggestedCommand.Execute(null);

        Assert.Equal(["recente"], Slugs(screen.Cards));
        Assert.Equal(["ancienne", "oubliee"], Slugs(screen.ArchivedTeams));
        Assert.Equal(Format(StudioStringKeys.TeamsArchivedManyUndo, 2), screen.UndoNotice);
        Assert.False(screen.HasArchiveSuggestion);
        // One gesture, one signal: the shell re-reads the other lists once, not once per team.
        Assert.Equal([$"{ancienne}|{oubliee}"], changed);

        screen.UndoCommand.Execute(null);

        Assert.Equal(3, screen.ActiveCount);
        Assert.Empty(screen.ArchivedTeams);
        Assert.False(screen.HasArchiveSuggestion);
        Assert.NotEmpty(screen.SuggestedTeams);
    }

    /// <summary>A listed team is read again at the click: one running, or open in the wizard, is refused on its card.</summary>
    [Fact]
    public void A_listed_team_that_is_busy_at_the_click_is_left_out_and_its_card_says_why()
    {
        var ancienne = Team("ancienne", "Ancienne", ranDaysAgo: 90);
        var oubliee = Team("oubliee", "Oubliée", ranDaysAgo: 61);
        var screen = Screen(
            undoDelay: new ManualUiDelay(),
            activityOf: path => path == ancienne ? TeamActivity.Running : TeamActivity.None);

        screen.ArchiveSuggestedCommand.Execute(null);

        Assert.False(TeamCatalog.Describe(ancienne).IsArchived);
        Assert.True(TeamCatalog.Describe(oubliee).IsArchived);
        Assert.Equal(Text(StudioStringKeys.TeamsArchiveBusy), Assert.Single(screen.Teams).ArchiveNotice);
        Assert.Equal(Format(StudioStringKeys.TeamsArchivedUndo, "Oubliée"), screen.UndoNotice);
    }

    [Fact]
    public void Dismissing_the_suggestion_hides_it_for_the_session_and_archives_nothing()
    {
        var ancienne = Team("ancienne", ranDaysAgo: 90);
        var screen = Screen();

        screen.DismissSuggestionCommand.Execute(null);
        screen.Refresh();
        screen.RefreshArchiveSuggestion();

        Assert.False(screen.HasArchiveSuggestion);
        Assert.False(TeamCatalog.Describe(ancienne).IsArchived);
    }

    /// <summary>
    /// The history may know a later run than the sidecar: nothing is proposed before it was read.
    /// Then the proposal follows Settings › Studio — the threshold, and the switch that turns it off.
    /// </summary>
    [Fact]
    public async Task The_suggestion_waits_for_the_history_and_follows_the_studio_settings()
    {
        var veille = Team("veille", "Veille", ranDaysAgo: 90);
        var history = new FakeLaunchHistoryStore
        {
            History = LaunchHistory.Empty.Add(new LaunchHistoryEntry { Target = veille, StartedAt = Now.AddDays(-45) }),
        };
        var settings = StudioSettings.Default;
        var screen = Screen(() => settings, history: history);

        Assert.False(screen.HasArchiveSuggestion);

        await screen.LoadLastRunsAsync(TestContext.Current.CancellationToken);
        Assert.False(screen.HasArchiveSuggestion);

        settings = settings with { ArchiveSuggestionDays = 30 };
        screen.RefreshArchiveSuggestion();
        Assert.True(screen.HasArchiveSuggestion);
        Assert.Equal(Format(StudioStringKeys.TeamsSuggestionOne, "Veille", 30), screen.ArchiveSuggestionLine);

        settings = settings with { ArchiveSuggestion = false };
        screen.RefreshArchiveSuggestion();
        Assert.False(screen.HasArchiveSuggestion);
        Assert.Empty(screen.SuggestedTeams);
    }

    /// <summary>
    /// One question on screen at a time (STUDIO-28's rule): a card's question makes the suggestion
    /// step aside until it is answered; an archive's undo takes the suggestion's place; and a card's
    /// question closes the undo banner — the archive stands.
    /// </summary>
    [Fact]
    public async Task One_question_at_a_time_the_undo_the_suggestion_and_the_card_banners_never_stack()
    {
        Team("ancienne", ranDaysAgo: 90);
        Team("veille", ranDaysAgo: 1);
        var rapport = Team("rapport", ranDaysAgo: 2);
        var undoDelay = new ManualUiDelay();
        var screen = Screen(undoDelay: undoDelay);
        Assert.True(screen.HasArchiveSuggestion);

        var veille = screen.Cards.Single(card => card.Slug == "veille");
        veille.AskDeleteCommand.Execute(null);
        Assert.False(screen.HasArchiveSuggestion);
        veille.CancelDeleteCommand.Execute(null);
        Assert.True(screen.HasArchiveSuggestion);

        await screen.Cards.Single(card => card.Slug == "rapport").ArchiveCommand.ExecuteAsync();
        Assert.True(screen.HasUndo);
        Assert.False(screen.HasArchiveSuggestion);

        screen.Cards.Single(card => card.Slug == "veille").AskDeleteCommand.Execute(null);
        Assert.False(screen.HasUndo);
        Assert.Equal(0, undoDelay.Pending);
        Assert.True(TeamCatalog.Describe(rapport).IsArchived);
    }

    // ── what STUDIO-31 handed over ──

    /// <summary>
    /// Undoing « Stop the schedule and archive » restores the team, not its schedule: the banner says
    /// so, and the card comes back on STUDIO-27's « not installed » state with its « Install » — the
    /// engine is never asked to reinstall anything behind the user's back.
    /// </summary>
    [Fact]
    public async Task Undoing_a_schedule_stop_brings_the_team_back_with_its_schedule_not_installed()
    {
        var veille = Team("veille", "Veille", schedule: "daily@07:30");
        var processes = new FakeProcessLauncher();
        processes.OutputToEmit.Add(Out("""{"v":2,"seq":1,"ts":"t","kind":"schedule.state","path":"p","state":"absent","reason":"not-installed","removed":true}"""));
        var screen = Screen(undoDelay: new ManualUiDelay(), processes: processes);

        await Assert.Single(screen.Cards).StopScheduleAndArchiveCommand.ExecuteAsync();

        Assert.True(TeamCatalog.Describe(veille).IsArchived);
        Assert.Equal(Format(StudioStringKeys.TeamsArchivedScheduleStoppedUndo, "Veille"), screen.UndoNotice);

        screen.UndoCommand.Execute(null);

        var card = Assert.Single(screen.Cards);
        Assert.False(card.IsArchived);
        Assert.Equal("daily@07:30", TeamCatalog.Describe(veille).Schedule);
        Assert.True(card.IsScheduled);
        Assert.Equal(TeamScheduleState.Absent, card.ScheduleState);
        Assert.Equal(Text(StudioStringKeys.TeamsScheduleAbsent), card.ScheduleStateLine);
        Assert.True(card.ShowsInstallSchedule);
        Assert.Equal(["forge", "unschedule", veille, "--events", "jsonl"], Assert.Single(processes.Requests).Arguments);
    }

    private static LaunchTabViewModel Launcher(string target, FakeProcessLauncher processes) =>
        new(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithDirectory(target).WithDirectory(Path.Combine(target, "agents")),
            Directories = new FakeDirectoryProbe(target),
            HistoryStore = new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
        });

    /// <summary>
    /// A team archived where the launcher could not see it — its sidecar edited by hand, a second
    /// Studio window — used to stay launchable until its target was picked again. The archived state
    /// is read again right before the launch: nothing runs, and the banner offers the restore.
    /// </summary>
    [Fact]
    public async Task A_team_archived_behind_the_launchers_back_is_refused_at_launch()
    {
        var veille = Team("veille");
        var processes = new FakeProcessLauncher();
        var launcher = Launcher(veille, processes);
        launcher.Target.Select(veille);
        Assert.True(launcher.RunCommand.CanExecute(null));

        TeamCatalog.Archive(veille, Now);
        await launcher.RunAsync(TestContext.Current.CancellationToken);
        await launcher.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(processes.Requests);
        Assert.True(launcher.IsTargetArchived);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), launcher.ArchivedTargetMessage);
        Assert.Equal(Text(StudioStringKeys.CommonArchivedTeamRestore), launcher.StatusMessage);
        Assert.False(launcher.RunCommand.CanExecute(null));
    }

    /// <summary>The Test screen launches through its own launcher, and reads the archive again the same way.</summary>
    [Fact]
    public async Task The_test_screen_reads_the_archive_again_before_its_trial()
    {
        var veille = Team("veille");
        var processes = new FakeProcessLauncher();
        var test = new TestTeamViewModel(Launcher(veille, processes), TeamsRoot);
        test.SelectedTeam = Assert.Single(test.TeamChoices);
        // Picked from the list, the team is resolved and ready, as from the card's Test icon.
        Assert.True(test.Launcher.RunCommand.CanExecute(null));

        TeamCatalog.Archive(veille, Now);
        await test.Launcher.RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(processes.Requests);
        Assert.True(test.Launcher.IsTargetArchived);
    }

    /// <summary>
    /// A copy's arrival is activity: « Duplicate » on a team nobody launched for a year gives a team
    /// that tops the list, which the suggestion does not propose, while its original is proposed.
    /// </summary>
    [Fact]
    public void A_duplicate_of_an_old_team_is_a_fresh_team()
    {
        Team("recente", "Récente", ranDaysAgo: 3);
        Team("modele", "Modèle", ranDaysAgo: 365);
        var screen = Screen();

        screen.Cards.Single(card => card.Slug == "modele").DuplicateCommand.Execute(null);

        Assert.Equal(["modele-copy", "recente", "modele"], Slugs(screen.Cards));
        Assert.Equal(["modele"], Slugs(screen.SuggestedTeams));
        Assert.Null(screen.Cards[0].LastRun);
    }

    // ── the acceptance criteria (fiche §7) ──

    /// <summary>
    /// Fifty teams, forty of them archived: the screen opens on ten cards, the most recently active
    /// first; three letters find one; the sidebar says ten.
    /// </summary>
    [Fact]
    public void Fifty_teams_forty_archived_open_on_ten_cards_and_three_letters_find_one()
    {
        string[] active =
        [
            "Veille", "Rapport", "Factures", "Synthèse", "Courrier",
            "Agenda", "Budget", "Contrats", "Inventaire", "Juridique",
        ];
        for (var i = 0; i < active.Length; i++)
            Team($"active-{i:00}", active[i], ranDaysAgo: i + 1);
        for (var i = 0; i < 40; i++)
            Team($"archivee-{i:00}", $"Archive {i:00}", ranDaysAgo: 100 + i, archived: true);

        var screen = Screen();

        Assert.Equal(active, screen.Cards.Select(card => card.Name));
        Assert.Equal(10, screen.ActiveCount);
        Assert.Equal(40, screen.ArchivedCount);
        Assert.Equal(Format(StudioStringKeys.TeamsArchivesLabel, 40), screen.ArchivesLabel);

        screen.SearchText = "inv";

        Assert.Equal(["Inventaire"], screen.Cards.Select(card => card.Name));
        Assert.Equal(10, screen.ActiveCount);
    }

    // ── Settings › Studio (DB-1, D-05) ──

    /// <summary>
    /// The switch and the threshold live in Settings › Studio, written through the same merge as the
    /// balance settings, which they keep. A threshold written by hand that the list does not offer is
    /// offered too, in its place.
    /// </summary>
    [Fact]
    public void The_suggestion_is_set_in_settings_studio_and_written_with_the_balance_settings()
    {
        var persisted = new List<StudioSettings>();
        var studio = new StudioSettingsViewModel(
            new BalanceReadings(settings: StudioSettings.Default with { BalanceRefreshMinutes = 15 }), persisted.Add);

        Assert.True(studio.ArchiveSuggestion);
        Assert.Equal(60, studio.SelectedArchiveSuggestion.Days);
        Assert.Equal([30, 60, 90, 180, 365], studio.ArchiveSuggestionChoices.Select(choice => choice.Days));
        Assert.Equal(Format(StudioStringKeys.SettingsArchiveSuggestionDays, 60), studio.SelectedArchiveSuggestion.Label);

        studio.SelectedArchiveSuggestion = studio.ArchiveSuggestionChoices.Single(choice => choice.Days == 90);
        studio.ArchiveSuggestion = false;

        Assert.Equal(90, persisted[^1].ArchiveSuggestionDays);
        Assert.False(persisted[^1].ArchiveSuggestion);
        Assert.Equal(15, persisted[^1].BalanceRefreshMinutes);
        Assert.Same(persisted[^1], studio.Current);

        var handEdited = new StudioSettingsViewModel(
            new BalanceReadings(settings: StudioSettings.Default with { ArchiveSuggestionDays = 45 }));
        Assert.Equal(45, handEdited.SelectedArchiveSuggestion.Days);
        Assert.Equal([30, 45, 60, 90, 180, 365], handEdited.ArchiveSuggestionChoices.Select(choice => choice.Days));
    }

    // ── the shell ──

    /// <summary>
    /// The window hands My teams Settings › Studio and a timer of its own for the undo banner: the
    /// suggestion follows the switch at once, and the chat's timer is never the one the banner uses.
    /// </summary>
    [Fact]
    public async Task The_shell_gives_my_teams_the_studio_settings_and_an_undo_timer_of_its_own()
    {
        Team("ancienne", ranDaysAgo: 90);
        Team("rapport", ranDaysAgo: 1);
        var chat = new ManualUiDelay();
        var undo = new ManualUiDelay();
        var window = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                Clock = new StubTimeProvider { Now = Now },
                Delay = chat,
                UndoDelay = undo,
            },
            globalPathOverride: Path.Combine(_root, "appsettings.json"),
            forgeWorkspace: _root,
            teamsRoot: TeamsRoot);
        await window.Teams.LoadLastRunsAsync(TestContext.Current.CancellationToken);
        Assert.True(window.Teams.HasArchiveSuggestion);

        window.Settings.Studio.ArchiveSuggestion = false;
        Assert.False(window.Teams.HasArchiveSuggestion);
        window.Settings.Studio.ArchiveSuggestion = true;
        Assert.True(window.Teams.HasArchiveSuggestion);

        var chatRequests = chat.Requested.Count;
        await window.Teams.Cards.Single(card => card.Slug == "rapport").ArchiveCommand.ExecuteAsync();

        Assert.True(window.Teams.HasUndo);
        Assert.Single(undo.Requested);
        Assert.Equal(chatRequests, chat.Requested.Count);
        Assert.Equal(1, window.Teams.ActiveCount);
    }
}
