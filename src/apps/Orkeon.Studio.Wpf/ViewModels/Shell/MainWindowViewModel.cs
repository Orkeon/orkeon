using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Teams;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The window: the two tabs of spec §3, and nothing else. Everything either tab does is a rendering
/// of <c>Orkeon.Studio.Core</c>, which is what keeps the WPF front-end and the two TUIs from drifting
/// apart — a feature that is not in Core exists in no UI.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private readonly IPathPicker _picker;
    private int _selectedTabIndex;

    /// <summary>
    /// Builds the window over its seams; the tests construct it entirely in memory.
    /// <paramref name="globalPathOverride"/>, <paramref name="forgeWorkspace"/> and
    /// <paramref name="teamsRoot"/> are the three roots Studio reads and writes under, each
    /// falling back to the per-user location when it is not named.
    /// </summary>
    public MainWindowViewModel(
        StudioServices? services = null,
        StudioUiPreferences? preferences = null,
        string? globalPathOverride = null,
        string? forgeWorkspace = null,
        string? teamsRoot = null)
    {
        var seams = services ?? new StudioServices();
        var ui = preferences ?? new StudioUiPreferences();

        // The seams are named once here: the wiring below hands most of them to three or four
        // screens apiece, and a local keeps the record itself out of every one of those calls.
        var settingsStore = seams.SettingsStore;
        var directories = seams.Directories;
        var targetProbe = seams.TargetProbe;
        var picker = seams.Picker;
        var historyStore = seams.HistoryStore;
        var dispatcher = seams.Dispatcher;
        var strings = seams.Strings;
        var profileStore = seams.ProfileStore;
        var shellOpener = seams.ShellOpener;
        var delay = seams.Delay;
        var llmProbe = seams.LlmProbe;
        // One key store for the profiles, the tool keys and the balance reads: the balance of an
        // account is read with the key its profile's row just remembered.
        var keyStore = seams.KeyStore ?? new EnvironmentApiKeyStore();
        var runner = seams.ProcessRunner ?? OrkeonProcessRunner.ForCurrentMachine();
        // One forge client for the wizard and the team cards' schedule gestures (STUDIO-27), over
        // the same binary as the doctor and the launcher when none is handed in.
        var forgeClient = seams.ForgeClient ?? ForgeClient.Over(runner);

        Mode = new UiModeViewModel(ui.InitialMode, ui.PersistMode);

        // The language resolves before anything reads a string: an explicit choice from
        // last time wins, otherwise the machine decides — and a detected language is
        // applied, never written down (T-13).
        Language = new LanguageSelectorViewModel(
            ui.InitialLanguage, ui.SystemLanguage, ui.ApplyLanguage, ui.PersistLanguage, strings);

        // ONE conversation for the window (T-01). Create, Run and History all mount
        // the same instance: recreated per screen, its history would die on the first tab
        // change — which is exactly the defect the thread was introduced to fix.
        Chat = new ChatThreadViewModel(strings, delay);
        About = new AboutViewModel(runner, dispatcher);

        var teamsHome = teamsRoot ?? TeamCatalog.DefaultRoot();
        // The forge workspace defaults to the per-user config directory (%APPDATA%\Orkeon
        // on Windows — where the global appsettings, the model profiles and the history
        // already live): sessions are resumable app state, not documents, unlike the
        // adopted teams which stay under ~/Orkeon/teams. Never the process working
        // directory: launched from the installed app or a dev tree, that is the
        // executable's bin folder — sessions would land in bin/.orkeon and vanish on the
        // next clean, and the assistant's /workspace would show DLLs.
        var forgeHome = TeamCatalog.EnsureDirectory(forgeWorkspace ?? DefaultForgeHome(teamsHome));

        // The tab is built over the window's own seams, the shared runner included: the doctor
        // panel and the launcher must never disagree about which binary is in use. The LLM probe
        // travels with them rather than being hard-coded null — the screenshot campaign passes
        // one that answers offline, which is what makes "the connection was tested"
        // photographable AND makes a live HTTP call from a headless run structurally impossible.
        // The two roots are the diagnostic's orphan-session list (STUDIO-27, D-08).
        Config = new ConfigTabViewModel(seams with { ProcessRunner = runner }, globalPathOverride, forgeHome, teamsHome);

        // The settings' folder list is read live everywhere it is needed: a team folder that is
        // not in it reads red — on the wizard's chips, the team cards and the team-mounts modal
        // alike — and stops the run outright. Passing a snapshot would leave a stale verdict
        // behind after an edit in the settings.
        Func<IReadOnlyList<string>> declaredMounts = () => Config.Mounts.CurrentMountStrings;

        Launch = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = runner,
            TargetProbe = targetProbe,
            Directories = directories,
            Picker = picker,
            HistoryStore = historyStore,
            SettingsStore = settingsStore,
            Dispatcher = dispatcher,
            Strings = strings,
            EnvironmentForTarget = TeamEnvironment,
            ShellOpener = shellOpener,
            DeclaredMounts = declaredMounts,
            Clipboard = seams.Clipboard,
            Clock = seams.Clock,
            // STUDIO-31: a real run stamps its team's last run under the teams root (D-05), and the
            // archived-team banner restores through « My teams », which owns the rules.
            TeamsRoot = teamsHome,
            RestoreTeam = RestoreArchivedTeam,
        });

        // VFS-90: each settings row says which teams name it by id, and removing one asks
        // first — the composition root supplies the question, the view model the facts. Every
        // team counts, archived ones too (STUDIO-31, D-08): removing a folder an archived team
        // names would break it on the day it is restored.
        Config.Mounts.LoadTeams = () => TeamCatalog.List(teamsHome, TeamListFilter.All);
        // STUDIO-35: the provider balances, read on the triggers of D-02 and kept in memory only
        // (D-05). The status bar, the profile rows and the profile editor share them; without a
        // probe in the seams nothing is read (see StudioServices.BalanceProbe).
        Balances = new BalanceReadings(seams.BalanceProbe, keyStore, ui.InitialStudio, seams.Clock);

        Settings = new SettingsScreenViewModel(
            Config,
            // « Used by » counts the archived teams too (STUDIO-31, D-08).
            new ModelProfilesViewModel(profileStore, Config.Llm, strings, llmProbe, keyStore,
                loadTeams: () => TeamCatalog.List(teamsHome, TeamListFilter.All),
                balances: Balances,
                shellOpener: shellOpener),
            Mode,
            // STUDIO-14 settings (D-13, P-1): the folders tab also lists each adopted team's own
            // folders, read from the sidecars and written nowhere — a team's folders are vouched
            // for by living inside it, and the global appsettings never learns them. Every team,
            // its archived state said (STUDIO-31, D-08).
            new TeamFoldersViewModel(() => TeamCatalog.List(teamsHome, TeamListFilter.All), strings, declaredMounts),
            // STUDIO-21: the tool keys ride the same store as the profile keys.
            new ToolsSettingsViewModel(keyStore, strings),
            // STUDIO-35 D-06: Settings › Studio, written into ui-preferences.json by merge.
            new StudioSettingsViewModel(Balances, ui.PersistStudio, strings));

        CreateTeam = new CreateTeamViewModel(
            Settings.Profiles,
            new CreateTeamDependencies
            {
                Client = forgeClient,
                Dispatcher = dispatcher,
                Strings = strings,
                WorkspaceDirectory = forgeHome,
                TeamsRoot = teamsRoot,
                DeclaredMounts = declaredMounts,
                Chat = this.Chat,
                // « Open the folder » in the wizard's header (STUDIO-14, D-15) — the same
                // opener the team cards use, gated the same way.
                ShellOpener = shellOpener,
                // STUDIO-39: the gallery reads the catalogue of the binary every other screen
                // runs, through the same runner; the suggestions pause on a timer of their own;
                // the problem a chosen case writes is read in the language the window speaks.
                UseCases = seams.UseCases ?? new UseCaseClient(runner),
                SuggestionDelay = seams.SuggestionDelay,
                UiLanguage = () => Language.Current,
                // STUDIO-41: the gallery's « Import as is » follows the window's expert switch.
                Mode = Mode,
                // STUDIO-32: an imported case dates its arrival — its first activity.
                Clock = seams.Clock,
            });

        Teams = new TeamsViewModel(new TeamsDependencies
        {
            TeamsRoot = teamsRoot,
            WorkspaceDirectory = forgeHome,
            Strings = strings,
            ShellOpener = shellOpener,
            HistoryStore = historyStore,
            DeclaredMounts = declaredMounts,
            Forge = forgeClient,
            // STUDIO-28 (D-02), STUDIO-31 (D-09): a team running, under test or open in the
            // wizard does not move — read at the moment of the gesture.
            ActivityOf = TeamActivityOf,
            Clock = seams.Clock,
            // STUDIO-32: the archive suggestion reads Settings › Studio as in force (DB-1), and the
            // undo banner keeps a timer of its own (D-02).
            StudioSettings = () => Settings.Studio.Current,
            UndoDelay = seams.UndoDelay,
        });


        // The expert trial screen runs over its own launcher, with NO history store: a
        // trial is a rehearsal, not a run to replay from the history.
        Test = new TestTeamViewModel(
            new LaunchTabViewModel(new LaunchTabDependencies
            {
                ProcessRunner = runner,
                TargetProbe = targetProbe,
                Directories = directories,
                Picker = picker,
                SettingsStore = settingsStore,
                Dispatcher = dispatcher,
                Strings = strings,
                EnvironmentForTarget = TeamEnvironment,
                ShellOpener = shellOpener,
                DeclaredMounts = declaredMounts,
                Clock = seams.Clock,
                // No teams root: a trial stamps no last run (STUDIO-31, D-05). The restore goes
                // through « My teams » like the Run screen's.
                RestoreTeam = RestoreArchivedTeam,
            }),
            teamsRoot);

        // STUDIO-34: the bar at the foot of the window watches the three activities that can run
        // at once — each with its own engine — and gives each a group while it runs (DD-2).
        StatusBar = new StatusBarViewModel(new StatusBarSources
        {
            Launch = Launch,
            Test = Test.Launcher,
            Atelier = CreateTeam.Progress,
            Profiles = Settings.Profiles,
            Mode = Mode,
            Strings = strings,
            Ticker = seams.Ticker,
            // STUDIO-35: the Balance segment covers the default profile, the assistant's and the
            // profiles the ACTIVE teams name (STUDIO-31, D-08): an archived team runs nowhere.
            Balances = Balances,
            TeamProfiles = () => TeamCatalog.List(teamsHome, TeamListFilter.Active).Select(team => team.Profile),
            BalanceTicker = seams.BalanceTicker,
            ShellOpener = shellOpener,
        });

        Import = new ImportTeamViewModel(new ImportTeamDependencies
        {
            TargetProbe = targetProbe,
            Picker = picker,
            Strings = strings,
            TeamsRoot = teamsRoot,
            // VFS-90 D-06: an imported team naming declarations this machine does not have can
            // have them authorized as recorded — under the same ids — from the review card.
            DeclaredMounts = declaredMounts,
            DeclareMount = Config.Mounts.AddPickedMount,
            SaveSettings = () => Config.SaveAsync(),
            // STUDIO-32: an imported team dates its arrival — its first activity.
            Clock = seams.Clock,
        });

        // Declaring a folder is the OS folder dialog, and that gesture belongs to the settings
        // and to the wizard's « Existing folders » rows (STUDIO-19). A team ASSOCIATES a folder
        // already declared there, which is what the chooser offers; wiring both team screens on
        // a declaration made every team re-declare its mounts from scratch.
        _picker = picker ?? NullPathPicker.Instance;
        AllowedFolders = new AllowedFolderChooserViewModel(declaredMounts, strings);
        TeamMounts = new TeamMountsDialogViewModel(strings, declaredMounts: declaredMounts);
        Teams.MountsRequested += (_, e) =>
            TeamMounts.Open(e.Card.Summary.Path, e.Card.Name, e.Card.Mounts,
                onSaved: () => { Teams.Refresh(); Launch.RefreshTeamDescription(); });
        Launch.RunRecorded += (_, _) => _ = Teams.LoadLastRunsAsync();
        Teams.TestRequested += (_, e) => { Test.Launcher.Target.Select(e.Path); TestRequested?.Invoke(this, EventArgs.Empty); };
        var effectiveStrings = strings ?? EnglishStudioStrings.Instance;
        _strings = effectiveStrings;
        Teams.ExportDestinationPicker = () =>
            picker?.PickFolder(effectiveStrings[Orkeon.Studio.Core.Localization.StudioStringKeys.DialogExportDestination]);
        TeamMounts.AddRequested += (_, _) =>
            AllowedFolders.Open([.. TeamMounts.Rows.Select(r => r.MountString)], TeamMounts.AddMount);
        // Two gestures, one modal. Without a target the chooser adds the settings entry as
        // declared; with one it answers that mount point, and the picked folder is bound
        // behind the name the agents actually use.
        CreateTeam.AllowFolderRequested += (_, e) =>
            AllowedFolders.Open(
                [.. CreateTeam.TeamMounts],
                e.TargetVirtualPath is { } target
                    ? mount => CreateTeam.BindTeamMount(target, mount)
                    : CreateTeam.AddTeamMount,
                e.TargetVirtualPath);
        // STUDIO-14 (D-10), STUDIO-19: « Existing folders » at step 1 asks for a REAL folder, so
        // the wizard opens the OS folder dialog itself; the pick is declared in the settings on
        // the way, under the row's rights, then bound behind the row. One gesture, no modal in
        // between; the declared list above stays the other way in.
        CreateTeam.PickFolderRequested += (sender, e) => _ = PickDeclareAndBindAsync(e.TargetVirtualPath, e.Rights);
        // The declare-a-new-folder action lands on the folders tab, not merely on the settings
        // screen: arriving on the model tab and having to find the right one is how the gesture
        // loses the user it was meant to help.
        AllowedFolders.OpenSettingsRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);
        // STUDIO-13: « Ouvrir le diagnostic » on the wizard's failure card re-runs the doctor
        // on arrival — a verdict taken at startup could still say «everything is in place»
        // over an engine that just failed. The window brings the screen forward itself.
        CreateTeam.OpenDiagnosticRequested += (_, _) => Config.Diagnostic.RunCommand.Execute(null);
        Launch.OpenAllowedFoldersRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);
        Test.Launcher.OpenAllowedFoldersRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);

        // An adopted team is an ordinary folder: the Launch action hands it to the launcher, the
        // adoption or an import refreshes the lists, a stopped session resumes in the wizard.
        Teams.LaunchRequested += (_, e) => Launch.Target.Select(e.Path);
        Teams.ResumeRequested += (sender, e) => _ = ResumeGuarded(e.Session);
        Teams.ModifyRequested += (sender, e) => _ = ModifyGuarded(e);
        // A draft discarded from My teams is gone from the disk: the wizard it was open on
        // goes back to a blank step 1 rather than keep a session that no longer exists.
        Teams.SessionDeleted += (_, e) => CreateTeam.ForgetSession(e.Session.Directory);
        // STUDIO-28: a renamed team's launches were rewritten in the history (D-04) — the Run
        // screen's list reloads — and a launcher aimed at the former folder follows the team.
        Teams.TeamRenamed += (_, e) =>
        {
            _ = Launch.History.LoadAsync();
            Launch.FollowRenamedTeam(e.From, e.Path);
            Test.Launcher.FollowRenamedTeam(e.From, e.Path);
            Test.RefreshTeams();
        };
        // An adoption or an import may bring a schedule: its card asks the engine where it stands
        // (STUDIO-27, D-05) — and the wizard's « Install » says what it did.
        CreateTeam.TeamAdopted += (_, e) => { Teams.Refresh(); Test.RefreshTeams(); _ = Teams.CheckScheduleAsync(e.Path); };
        // STUDIO-31 (D-08): an archive or a restore changes what the Test picker offers, and what
        // the two launchers may run — a target archived under them is refused from then on.
        Teams.ArchiveChanged += (_, _) =>
        {
            Test.RefreshTeams();
            Launch.RefreshTeamDescription();
            Test.Launcher.RefreshTeamDescription();
        };
        CreateTeam.ScheduleOffer.ScheduleChanged += (_, e) => Teams.RecordScheduleState(e.Path, e.State);
        // STUDIO-32 (DB-1): the archive suggestion follows Settings › Studio at once — turned off, it
        // leaves My teams; a new threshold, and it is worked out again.
        Settings.Studio.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioSettingsViewModel.Current))
                Teams.RefreshArchiveSuggestion();
        };
        Import.TeamImported += (_, e) => { Teams.Refresh(); Test.RefreshTeams(); _ = Teams.CheckScheduleAsync(e.Path); };
        // A use case imported as it is from the gallery (STUDIO-41) has no schedule to ask about.
        CreateTeam.Gallery.Import.TeamImported += (_, _) => { Teams.Refresh(); Test.RefreshTeams(); };
        // STUDIO-14 settings (D-13): the « Team folders » section of the settings follows the
        // team list. Every change to the teams on disk — an adoption, an import, a deletion or
        // a duplication from a card, a save of the folders modal — ends in Teams.Refresh(),
        // which rebuilds the cards from an empty list; that Reset fires once per rebuild, after
        // the disk has changed, and the section re-reads the sidecars on it. One signal for
        // every gesture, including the two card actions that raise no event of their own.
        Teams.Teams.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                Settings.TeamFolders.Refresh();
                Config.Mounts.RefreshTeamReferences();
                // A team adopted, deleted or pointed at another profile changes what the
                // Balance segment covers (STUDIO-35 D-01) — said now, read on the next trigger.
                StatusBar.Balance.Rebuild();
            }
        };
        // STUDIO-18: the declared folders are read live from the settings editor, but a team
        // card computes its chips' verdicts when it is built and the launcher when its target
        // is picked. A folder declared (or dropped) in « Settings › Authorized folders » must
        // reach them without waiting for an adoption or a restart — the same three refreshes
        // a save of the team-mounts modal already runs.
        Config.Mounts.Changed += (_, _) =>
        {
            Teams.Refresh();
            Launch.RefreshTeamDescription();
            Test.Launcher.RefreshTeamDescription();
        };
    }

    /// <summary>The appsettings editor (spec §4).</summary>
    public ConfigTabViewModel Config { get; }

    /// <summary>The unified Settings screen and its model profiles (design v3).</summary>
    public SettingsScreenViewModel Settings { get; }

    /// <summary>The crew launcher (spec §5).</summary>
    public LaunchTabViewModel Launch { get; }

    /// <summary>The create-a-team wizard, over the forge engine (design v3).</summary>
    /// <summary>
    /// The conversation with the assistant, shared by Create, Run and History.
    /// Bound through the window ancestor on the screens that do not own it, so a change of
    /// tab hands the same thread to the next screen rather than a fresh, empty one.
    /// </summary>
    /// <summary>Which language the app speaks, and whether that was a choice (T-13/T-14).</summary>
    public LanguageSelectorViewModel Language { get; }

    public ChatThreadViewModel Chat { get; }

    public CreateTeamViewModel CreateTeam { get; }

    /// <summary>The My teams screen — the adopted team folders and the sessions underway.</summary>
    public TeamsViewModel Teams { get; }

    /// <summary>The expert Test screen — a trial launcher that never touches the history.</summary>
    public TestTeamViewModel Test { get; }

    /// <summary>The bar at the foot of the window (STUDIO-34): one group per activity while it runs.</summary>
    public StatusBarViewModel StatusBar { get; }

    /// <summary>The provider balances read this session (STUDIO-35), shared by the bar, the profile rows and the editor.</summary>
    public BalanceReadings Balances { get; }

    /// <summary>The Import screen.</summary>
    public ImportTeamViewModel Import { get; }

    /// <summary>
    /// The add-an-allowed-folder modal the two team screens use: it offers the folders
    /// already declared in the settings, and its « Declare a new folder… » sends the user to
    /// the settings' folders tab to declare one more.
    /// </summary>
    public AllowedFolderChooserViewModel AllowedFolders { get; }

    /// <summary>The folders-of-team-X modal (remediation v2, F-03).</summary>
    public TeamMountsDialogViewModel TeamMounts { get; }

    /// <summary>Raised when a team card asks for the trial screen — the shell switches tabs.</summary>
    public event EventHandler? TestRequested;

    /// <summary>The window-wide Novice/Expert switch (design v3).</summary>
    public UiModeViewModel Mode { get; }

    /// <summary>The About overlay state.</summary>
    public AboutViewModel About { get; }

    /// <summary>Which tab is showing.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    /// <summary>The window title.</summary>
    public static string Title => "Orkeon Studio";

    /// <summary>
    /// Builds a window wired to the real machine: the physical disk, the co-installed CLI and the
    /// per-user history file. The history store degrades to in-memory when the platform gives us no
    /// configuration directory, rather than refusing to open the window over it.
    /// </summary>
    public static MainWindowViewModel CreateForCurrentMachine(
        IPathPicker picker,
        IUiDispatcher dispatcher,
        StudioServices? services = null,
        StudioUiPreferences? preferences = null,
        string? teamsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var seams = services ?? new StudioServices();

        ILaunchHistoryStore? historyStore =
            LaunchHistoryFileStore.TryGetDefaultPath(out var historyPath, out _) && historyPath is { Length: > 0 }
                ? new LaunchHistoryFileStore(historyPath)
                : null;

        // The profiles come from the per-user file, unless the caller brought its own store.
        IModelProfileStore? profileStore = seams.ProfileStore;
        if (profileStore is null
            && ModelProfileFileStore.TryGetDefaultPath(out var profilePath, out _)
            && profilePath is { Length: > 0 })
        {
            profileStore = new ModelProfileFileStore(profilePath);
        }

        // Everything that touches the machine is this method's own: what the caller hands in are
        // the front-end seams (the strings, the shell opener, the timed delay) and, for a test
        // harness, a store it wants honoured.
        return new MainWindowViewModel(
            seams with
            {
                SettingsStore = PhysicalAppSettingsStore.Instance,
                Directories = PhysicalDirectoryProbe.Instance,
                TargetProbe = PhysicalTargetProbe.Instance,
                Picker = picker,
                ProcessRunner = OrkeonProcessRunner.ForCurrentMachine(),
                HistoryStore = historyStore,
                Dispatcher = dispatcher,
                ProfileStore = profileStore,
                // The one place a balance probe over the real network is named (STUDIO-35).
                BalanceProbe = seams.BalanceProbe ?? HttpProviderBalanceProbe.ForCurrentMachine(),
            },
            preferences,
            teamsRoot: teamsRoot);
    }

    /// <summary>Runs the work the window defers until it is shown: opening the per-user settings file, locating the CLI, loading the history, reading the model profiles.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Startup fault barrier: the caller discards this task, so an unexpected " +
                        "failure in one loader must land on a status line, never vanish or kill the window.")]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The settings come first, and alone (STUDIO-18): the folders they declare are read
            // live by everything below — the launcher's verdict on a restored target, the
            // effective-mount table — and the team cards computed theirs on an empty list when
            // the window was built. Nothing may look before the per-user file is in.
            await Config.InitializeAsync(cancellationToken).ConfigureAwait(true);
            if (Config.LoadedPath is not null)
                Teams.Refresh();

            await Task.WhenAll(
                Launch.InitializeAsync(cancellationToken),
                Test.Launcher.InitializeAsync(cancellationToken),
                LoadProfilesThenReadBalanceAsync(cancellationToken),
                // The silent doctor run (audit 09/20): the sidebar dot and the verdict card
                // are honest from the first frame, without the user pressing anything.
                Config.Diagnostic.InitializeAsync(cancellationToken),
                // The team cards' last-run line, from the same history the
                // History screen reads.
                Teams.LoadLastRunsAsync(cancellationToken),
                // The use-case catalogue (STUDIO-39): the wizard's link says how many cases it
                // holds from the first frame, and the suggestions have sheets to count against.
                CreateTeam.Gallery.LoadAsync(cancellationToken),
                // Where each scheduled team's schedule really stands — the engine says, the
                // sidecar never does (STUDIO-27, D-05).
                Teams.CheckSchedulesAsync(cancellationToken)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Shutdown mid-load: nothing to say.
        }
        catch (Exception ex)
        {
            CreateTeam.ReportStatus(ex.Message);
        }
    }

    /// <summary>
    /// The profiles, then the balance of the accounts they cover (STUDIO-35 D-02: the startup
    /// read) — which accounts to ask is not known before the profiles are in, and the other
    /// loaders need not wait on either.
    /// </summary>
    private async Task LoadProfilesThenReadBalanceAsync(CancellationToken cancellationToken)
    {
        await Settings.Profiles.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await StatusBar.Balance.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// A resume failure (a session directory gone unreadable) must land in the wizard's
    /// status line, not in a discarded task — the screen would otherwise come forward
    /// empty with no word of why.
    /// </summary>
    private async Task ResumeGuarded(Orkeon.Studio.Core.Forge.ForgeSolutionSummary session)
    {
        try
        {
            await CreateTeam.ResumeAsync(session).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever it was — the task is discarded, so this line is the only place it can
            // be said (owner report of 2026-09-21: a silent reopen reads as a dead button).
            CreateTeam.ReportStatus(ex.Message);
        }
    }

    /// <summary>
    /// The wizard's disk pick (STUDIO-19): the OS folder dialog — opened on the reopened team's
    /// folder when there is one — then the five steps of <see cref="DeclareAndBindAsync"/>. A
    /// cancelled dialog does nothing. The virtual name is derived the way the settings' own
    /// button derives it, unique against the settings entries and the team's.
    /// </summary>
    private async Task PickDeclareAndBindAsync(string? targetVirtualPath, MountRights rights)
    {
        var picked = _picker.PickFolder(_strings[StudioStringKeys.DialogSelectMountFolder], CreateTeam.ReopenedTeamPath);
        if (picked is not { Length: > 0 })
            return;

        // A targeted pick is declared under the ROW's root (VFS-90, D-01): the team names the
        // declaration, so the declaration must carry the name the agents use — a second entry
        // under a root another folder already spends is what ids exist for. Only the untargeted
        // pick derives a name.
        string virtualPath;
        if (targetVirtualPath is { Length: > 0 })
        {
            virtualPath = targetVirtualPath;
        }
        else
        {
            var taken = Config.Mounts.CurrentMountStrings.Concat(CreateTeam.TeamMounts)
                .Select(entry => MountDefinition.TryParse(entry, out var mount, out _) ? mount.VirtualPath : null)
                .OfType<string>();
            virtualPath = MountDefinition.SuggestVirtualPath(picked, taken);
        }

        await DeclareAndBindAsync(targetVirtualPath, rights, new MountDefinition
        {
            PhysicalPath = picked,
            VirtualPath = virtualPath,
            Rights = rights,
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// The five steps of a disk pick (STUDIO-14, D-10): a folder inside the reopened
    /// team is not declared — it is the team's own and the save relativizes it; any other
    /// folder is declared unless the settings already hold it (by physical folder — a second
    /// pick of one folder under other rights adds no second entry); the settings are saved
    /// every time, because the novice auto-save may be in flight and a second identical write
    /// is harmless and makes the outcome true; the team binds the folder under the ROW's
    /// rights — the settings entry takes the same, there is no separate choice any more; and the wizard's status line says
    /// which of the two things happened, a refused save included: the folder is bound and
    /// counts as declared for the verdicts (the editor's live list vouches for it), so the
    /// sentence is the only trace of the refusal.
    /// </summary>
    private async Task DeclareAndBindAsync(string? targetVirtualPath, MountRights rights, MountDefinition mount)
    {
        var bound = mount with { Rights = rights };
        if (DeclaredMounts.IsInsideTeam(mount.ToMountString(), CreateTeam.ReopenedTeamPath))
        {
            // The team's own folder: no id, no settings entry (D-07).
            Bind(targetVirtualPath, bound.WithoutId());
            return;
        }

        // The declaration the team will name (VFS-90): an equal entry is reused — and given an
        // id if it had none — otherwise the pick is added under an id of its own, even when
        // another entry already claims its root. The team then binds THAT entry, verbatim.
        var (declared, reused) = Config.Mounts.EnsureDeclared(bound);
        var saved = await Config.SaveAsync().ConfigureAwait(true);
        Bind(targetVirtualPath, declared);

        var folder = System.IO.Path.GetFileName(mount.PhysicalPath.TrimEnd('/', '\\')) is { Length: > 0 } name
            ? name
            : mount.PhysicalPath;
        string savedKey;
        if (reused)
            savedKey = StudioStringKeys.AllowedFoldersReused;
        else if (targetVirtualPath is { Length: > 0 })
            savedKey = StudioStringKeys.WizardDeclaredFolder;
        else
            savedKey = StudioStringKeys.AllowedFoldersDeclared;
        CreateTeam.ReportStatus(saved
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _strings[savedKey], folder, declared.VirtualPath)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, _strings[StudioStringKeys.AllowedFoldersNotSaved], folder, Config.StatusMessage));

        void Bind(string? target, MountDefinition picked)
        {
            if (target is { } virtualPath)
                CreateTeam.BindTeamMount(virtualPath, picked);
            else
                CreateTeam.AddTeamMount(picked);
        }
    }

    /// <summary>The Modify action on a team card — same fault barrier as a resume (W-09).</summary>
    private async Task ModifyGuarded(ViewModels.Teams.TeamModifyEventArgs request)
    {
        try
        {
            await CreateTeam.ReopenTeamAsync(request.Team).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever it was — the task is discarded, so this line is the only place it can
            // be said (owner report of 2026-09-21: a silent reopen reads as a dead button).
            CreateTeam.ReportStatus(ex.Message);
        }
    }

    /// <summary>
    /// The per-user config directory as the forge home, falling back to the Orkeon user
    /// home's parent when the platform yields no config directory (bare containers).
    /// </summary>
    private static string DefaultForgeHome(string teamsHome) =>
        Orkeon.Studio.Core.Storage.SettingsLocations.TryGetGlobalSettingsPath(out var settingsPath, out _)
            && System.IO.Path.GetDirectoryName(settingsPath) is { Length: > 0 } configDirectory
            ? configDirectory
            : System.IO.Path.GetDirectoryName(teamsHome) ?? teamsHome;

    /// <summary>
    /// What Studio is doing with a team folder (STUDIO-28, D-02; STUDIO-31, D-09): the run in
    /// flight of either launcher, and the team the wizard reopened. The Test screen is built after
    /// My teams, so the launchers are read when a gesture asks, never captured at construction.
    /// </summary>
    private TeamActivity TeamActivityOf(string teamPath) =>
        TeamActivities.Of(teamPath, Launch.RunningTarget, Test.Launcher.RunningTarget, CreateTeam.ReopenedTeamPath);

    /// <summary>
    /// The launchers' « Restore » on an archived target (STUDIO-31, D-07): through « My teams », so
    /// the rules (D-09) and the refresh of every list are one; the refusal, or null once restored.
    /// </summary>
    private string? RestoreArchivedTeam(string teamPath) => Teams.RestoreTeam(teamPath);

    /// <summary>
    /// The environment an adopted team lays over its launches: the sidecar names a model
    /// profile, the profile store resolves it to <c>ORKEON_Llm__*</c> overrides. Null for a
    /// target that is not a team or names no (or an unknown) profile — the launch then runs
    /// on the settings file, like any other.
    /// </summary>
    private IReadOnlyDictionary<string, string>? TeamEnvironment(string targetPath) =>
        Settings.Profiles.Set.Find(TeamCatalog.ProfileFor(targetPath))?.EnvironmentOverrides(Environment.GetEnvironmentVariable);
}
