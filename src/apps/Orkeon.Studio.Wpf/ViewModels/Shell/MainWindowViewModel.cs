using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
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
    private int _selectedTabIndex;

    /// <summary>Builds the window over its seams; the tests construct it entirely in memory.</summary>
    public MainWindowViewModel(
        IAppSettingsStore? settingsStore = null,
        IDirectoryProbe? directories = null,
        ITargetProbe? targetProbe = null,
        IPathPicker? picker = null,
        OrkeonProcessRunner? processRunner = null,
        ILaunchHistoryStore? historyStore = null,
        IUiDispatcher? dispatcher = null,
        string? globalPathOverride = null,
        IStudioStrings? strings = null,
        ForgeClient? forgeClient = null,
        string? forgeWorkspace = null,
        string? initialUiMode = null,
        Action<string>? persistUiMode = null,
        IModelProfileStore? profileStore = null,
        string? teamsRoot = null,
        IShellOpener? shellOpener = null)
    {
        var runner = processRunner ?? OrkeonProcessRunner.ForCurrentMachine();

        Mode = new UiModeViewModel(initialUiMode, persistUiMode);
        About = new AboutViewModel(runner, dispatcher);

        Config = new ConfigTabViewModel(
            settingsStore,
            directories,
            picker,
            runner,
            dispatcher,
            globalPathOverride,
            llmProbe: null,
            strings);

        Launch = new LaunchTabViewModel(
            runner,
            targetProbe,
            directories,
            picker,
            historyStore,
            settingsStore,
            dispatcher,
            strings,
            TeamEnvironment,
            shellOpener);

        var teamsHome = teamsRoot ?? TeamCatalog.DefaultRoot();
        // The forge workspace defaults to the per-user config directory (%APPDATA%\Orkeon
        // on Windows — where the global appsettings, the model profiles and the history
        // already live): sessions are resumable app state, not documents, unlike the
        // adopted teams which stay under ~/Orkeon/teams. Never the process working
        // directory: launched from the installed app or a dev tree, that is the
        // executable's bin folder — sessions would land in bin/.orkeon and vanish on the
        // next clean, and the assistant's /workspace would show DLLs.
        var forgeHome = TeamCatalog.EnsureDirectory(forgeWorkspace ?? DefaultForgeHome(teamsHome));
        Settings = new SettingsScreenViewModel(
            Config,
            new ModelProfilesViewModel(profileStore, Config.Llm, strings,
                loadTeams: () => TeamCatalog.List(teamsHome)),
            Mode);

        CreateTeam = new CreateTeamViewModel(
            Settings.Profiles,
            forgeClient,
            dispatcher,
            strings,
            forgeHome,
            teamsRoot);

        Teams = new TeamsViewModel(teamsRoot, forgeHome, strings: strings, shellOpener: shellOpener, historyStore: historyStore);


        // The expert trial screen runs over its own launcher, with NO history store: a
        // trial is a rehearsal, not a run to replay from the history.
        Test = new TestTeamViewModel(
            new LaunchTabViewModel(runner, targetProbe, directories, picker, null, settingsStore, dispatcher, strings, TeamEnvironment, shellOpener),
            teamsRoot);

        Import = new ImportTeamViewModel(targetProbe, picker, strings, teamsRoot);

        // The folder picker DECLARES a folder — a disk tree, a physical path, a rights choice.
        // That gesture belongs to the settings, and to nothing else. A team ASSOCIATES a folder
        // already declared there, which is what the chooser offers; wiring both team screens on
        // the picker made every team re-declare its mounts from scratch.
        FolderPicker = new FolderPickerViewModel(directories, picker, strings);
        AllowedFolders = new AllowedFolderChooserViewModel(() => Config.Mounts.CurrentMountStrings, strings);
        TeamMounts = new TeamMountsDialogViewModel(strings);
        Teams.MountsRequested += (_, e) =>
            TeamMounts.Open(e.Card.Summary.Path, e.Card.Name, e.Card.Mounts,
                onSaved: () => { Teams.Refresh(); Launch.RefreshTeamDescription(); });
        Launch.RunRecorded += (_, _) => _ = Teams.LoadLastRunsAsync();
        Teams.TestRequested += (_, e) => { Test.Launcher.Target.Select(e.Path); TestRequested?.Invoke(this, EventArgs.Empty); };
        var effectiveStrings = strings ?? Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance;
        Teams.ExportDestinationPicker = () =>
            picker?.PickFolder(effectiveStrings[Orkeon.Studio.Core.Localization.StudioStringKeys.DialogExportDestination]);
        TeamMounts.AddRequested += (_, _) =>
            AllowedFolders.Open([.. TeamMounts.Rows.Select(r => r.MountString)], TeamMounts.AddMount);
        CreateTeam.AllowFolderRequested += (_, _) =>
            AllowedFolders.Open([.. CreateTeam.TeamMounts], CreateTeam.AddTeamMount);
        // The settings ARE the declaration screen: theirs is the one button that still opens
        // the disk picker directly.
        Config.Mounts.FolderPickRequested += (_, _) =>
            FolderPicker.Open(Config.Mounts.CurrentMountStrings, Config.Mounts.AddPickedMount);
        AllowedFolders.DeclareRequested += (_, _) =>
            FolderPicker.Open(Config.Mounts.CurrentMountStrings, mount => _ = DeclareAllowedFolderAsync(mount));

        // An adopted team is an ordinary folder: "Lancer" hands it to the launcher, the
        // adoption or an import refreshes the lists, a stopped session resumes in the wizard.
        Teams.LaunchRequested += (_, e) => Launch.Target.Select(e.Path);
        Teams.ResumeRequested += (_, e) => _ = ResumeGuarded(e.Session);
        Teams.ModifyRequested += (_, e) => _ = ModifyGuarded(e);
        CreateTeam.TeamAdopted += (_, _) => { Teams.Refresh(); Test.RefreshTeams(); };
        Import.TeamImported += (_, _) => { Teams.Refresh(); Test.RefreshTeams(); };
    }

    /// <summary>The appsettings editor (spec §4).</summary>
    public ConfigTabViewModel Config { get; }

    /// <summary>The unified "Réglages" screen and its model profiles (design v3).</summary>
    public SettingsScreenViewModel Settings { get; }

    /// <summary>The crew launcher (spec §5).</summary>
    public LaunchTabViewModel Launch { get; }

    /// <summary>The "Créer une équipe" wizard, over the forge engine (design v3).</summary>
    public CreateTeamViewModel CreateTeam { get; }

    /// <summary>"Mes équipes" — the adopted team folders and the sessions underway.</summary>
    public TeamsViewModel Teams { get; }

    /// <summary>The expert "Tester" screen — a trial launcher that never touches the history.</summary>
    public TestTeamViewModel Test { get; }

    /// <summary>The "Importer" screen.</summary>
    public ImportTeamViewModel Import { get; }

    /// <summary>The shared « Autoriser un dossier » modal (remediation v2, F-03).</summary>
    public FolderPickerViewModel FolderPicker { get; }

    /// <summary>
    /// The « Ajouter un dossier autorisé » modal the two team screens use: it offers the folders
    /// already declared in the settings, and chains to <see cref="FolderPicker"/> to declare one more.
    /// </summary>
    public AllowedFolderChooserViewModel AllowedFolders { get; }

    /// <summary>The « Dossiers de « X » » modal (remediation v2, F-03).</summary>
    public TeamMountsDialogViewModel TeamMounts { get; }

    /// <summary>Raised when a team card asks for the trial screen — the shell switches tabs.</summary>
    public event EventHandler? TestRequested;

    /// <summary>The window-wide Novice/Expert switch (design v3).</summary>
    public UiModeViewModel Mode { get; }

    /// <summary>The "À propos" overlay state.</summary>
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
        IStudioStrings? strings = null,
        string? initialUiMode = null,
        Action<string>? persistUiMode = null,
        IModelProfileStore? profileStore = null,
        string? teamsRoot = null,
        IShellOpener? shellOpener = null)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(dispatcher);

        ILaunchHistoryStore? historyStore =
            LaunchHistoryFileStore.TryGetDefaultPath(out var historyPath, out _) && historyPath is { Length: > 0 }
                ? new LaunchHistoryFileStore(historyPath)
                : null;

        return new MainWindowViewModel(
            PhysicalAppSettingsStore.Instance,
            PhysicalDirectoryProbe.Instance,
            PhysicalTargetProbe.Instance,
            picker,
            OrkeonProcessRunner.ForCurrentMachine(),
            historyStore,
            dispatcher,
            globalPathOverride: null,
            strings,
            forgeClient: null,
            forgeWorkspace: null,
            initialUiMode,
            persistUiMode,
            ModelProfileFileStore.TryGetDefaultPath(out var profilePath, out _) && profilePath is { Length: > 0 }
                ? new ModelProfileFileStore(profilePath)
                : null,
            teamsRoot,
            shellOpener);
    }

    /// <summary>Runs the work the window defers until it is shown: locating the CLI, loading the history, reading the model profiles.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Startup fault barrier: the caller discards this task, so an unexpected " +
                        "failure in one loader must land on a status line, never vanish or kill the window.")]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.WhenAll(
                Launch.InitializeAsync(cancellationToken),
                Test.Launcher.InitializeAsync(cancellationToken),
                Settings.Profiles.InitializeAsync(cancellationToken),
                // The silent doctor run (audit 09/20): the sidebar dot and the verdict card
                // are honest from the first frame, without the user pressing anything.
                Config.Diagnostic.InitializeAsync(cancellationToken),
                // The team cards' "dernière exécution" line, from the same history the
                // Historique screen reads.
                Teams.LoadLastRunsAsync(cancellationToken)).ConfigureAwait(true);
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
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            CreateTeam.ReportStatus(ex.Message);
        }
    }

    /// <summary>
    /// A folder declared from the chooser lands in the settings AND on disk right away: the
    /// user is not in the settings' edit cycle when they make this gesture, and asking them to
    /// go and save afterwards is how a declaration gets lost. Novice mode already auto-saves on
    /// every edit; this makes Expert behave the same for this one gesture.
    /// <para>
    /// A refused save is reported, never swallowed: the folder is usable for the team either
    /// way — it is in the live document — but only the file was not written, and the modal says
    /// so with the settings screen's own message.
    /// </para>
    /// </summary>
    private async Task DeclareAllowedFolderAsync(Orkeon.Studio.Core.FileSystem.MountDefinition mount)
    {
        Config.Mounts.AddPickedMount(mount);

        var saved = await Config.SaveAsync().ConfigureAwait(true);
        AllowedFolders.NotifyDeclared(mount, saved ? null : Config.StatusMessage);
    }

    /// <summary>«Modifier» on a team card — same fault barrier as a resume (W-09).</summary>
    private async Task ModifyGuarded(ViewModels.Teams.TeamModifyEventArgs request)
    {
        try
        {
            await CreateTeam.ReopenTeamAsync(request.Team, request.Session).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
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
    /// The environment an adopted team lays over its launches: the sidecar names a model
    /// profile, the profile store resolves it to <c>ORKEON_Llm__*</c> overrides. Null for a
    /// target that is not a team or names no (or an unknown) profile — the launch then runs
    /// on the settings file, like any other.
    /// </summary>
    private IReadOnlyDictionary<string, string>? TeamEnvironment(string targetPath) =>
        Settings.Profiles.Set.Find(TeamCatalog.ProfileFor(targetPath))?.EnvironmentOverrides(Environment.GetEnvironmentVariable);
}
