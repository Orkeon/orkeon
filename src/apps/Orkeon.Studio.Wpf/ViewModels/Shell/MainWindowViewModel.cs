using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Llm;
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
        IShellOpener? shellOpener = null,
        IUiDelay? delay = null,
        string? initialLanguage = null,
        Action<string>? persistLanguage = null,
        Action<string>? applyLanguage = null,
        string? systemLanguage = null,
        ILlmEndpointProbe? llmProbe = null,
        IApiKeyStore? keyStore = null)
    {
        var runner = processRunner ?? OrkeonProcessRunner.ForCurrentMachine();

        Mode = new UiModeViewModel(initialUiMode, persistUiMode);

        // The language resolves before anything reads a string: an explicit choice from
        // last time wins, otherwise the machine decides — and a detected language is
        // applied, never written down (T-13).
        Language = new LanguageSelectorViewModel(
            initialLanguage, systemLanguage, applyLanguage, persistLanguage, strings);

        // ONE conversation for the window (T-01). Create, Run and History all mount
        // the same instance: recreated per screen, its history would die on the first tab
        // change — which is exactly the defect the thread was introduced to fix.
        Chat = new ChatThreadViewModel(strings, delay);
        About = new AboutViewModel(runner, dispatcher);

        Config = new ConfigTabViewModel(
            settingsStore,
            directories,
            picker,
            runner,
            dispatcher,
            globalPathOverride,
            // Injected rather than hard-coded null: the screenshot campaign passes a probe that
            // answers offline, which is what makes "the connection was tested" photographable AND
            // makes a live HTTP call from a headless run structurally impossible.
            llmProbe,
            strings);

        // The settings' folder list is read live everywhere it is needed: a team folder that is
        // not in it reads red — on the wizard's chips, the team cards and the team-mounts modal
        // alike — and stops the run outright. Passing a snapshot would leave a stale verdict
        // behind after an edit in the settings.
        Func<IReadOnlyList<string>> declaredMounts = () => Config.Mounts.CurrentMountStrings;

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
            shellOpener,
            declaredMounts);

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
            new ModelProfilesViewModel(profileStore, Config.Llm, strings, llmProbe, keyStore,
                loadTeams: () => TeamCatalog.List(teamsHome)),
            Mode);

        CreateTeam = new CreateTeamViewModel(
            Settings.Profiles,
            forgeClient,
            dispatcher,
            strings,
            forgeHome,
            teamsRoot,
            declaredMounts,
            Chat);

        Teams = new TeamsViewModel(
            teamsRoot, forgeHome, strings: strings, shellOpener: shellOpener, historyStore: historyStore,
            declaredMounts: declaredMounts);


        // The expert trial screen runs over its own launcher, with NO history store: a
        // trial is a rehearsal, not a run to replay from the history.
        Test = new TestTeamViewModel(
            new LaunchTabViewModel(
                runner, targetProbe, directories, picker, null, settingsStore, dispatcher, strings,
                TeamEnvironment, shellOpener, declaredMounts),
            teamsRoot);

        Import = new ImportTeamViewModel(targetProbe, picker, strings, teamsRoot);

        // The folder picker DECLARES a folder — a disk tree, a physical path, a rights choice.
        // That gesture belongs to the settings, and to nothing else. A team ASSOCIATES a folder
        // already declared there, which is what the chooser offers; wiring both team screens on
        // the picker made every team re-declare its mounts from scratch.
        FolderPicker = new FolderPickerViewModel(directories, picker, strings);
        AllowedFolders = new AllowedFolderChooserViewModel(declaredMounts, strings);
        TeamMounts = new TeamMountsDialogViewModel(strings, declaredMounts: declaredMounts);
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
        // The settings ARE the declaration screen: theirs is the one button that still opens
        // the disk picker directly.
        Config.Mounts.FolderPickRequested += (_, _) =>
            FolderPicker.Open(Config.Mounts.CurrentMountStrings, Config.Mounts.AddPickedMount);
        // The declare-a-new-folder action lands on the folders tab, not merely on the settings
        // screen: arriving on the model tab and having to find the right one is how the gesture
        // loses the user it was meant to help.
        AllowedFolders.OpenSettingsRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);
        Launch.OpenAllowedFoldersRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);
        Test.Launcher.OpenAllowedFoldersRequested += (_, _) => Settings.ShowFoldersCommand.Execute(null);

        // An adopted team is an ordinary folder: the Launch action hands it to the launcher, the
        // adoption or an import refreshes the lists, a stopped session resumes in the wizard.
        Teams.LaunchRequested += (_, e) => Launch.Target.Select(e.Path);
        Teams.ResumeRequested += (_, e) => _ = ResumeGuarded(e.Session);
        Teams.ModifyRequested += (_, e) => _ = ModifyGuarded(e);
        CreateTeam.TeamAdopted += (_, _) => { Teams.Refresh(); Test.RefreshTeams(); };
        Import.TeamImported += (_, _) => { Teams.Refresh(); Test.RefreshTeams(); };
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

    /// <summary>The Import screen.</summary>
    public ImportTeamViewModel Import { get; }

    /// <summary>The shared allow-a-folder modal (remediation v2, F-03).</summary>
    public FolderPickerViewModel FolderPicker { get; }

    /// <summary>
    /// The add-an-allowed-folder modal the two team screens use: it offers the folders
    /// already declared in the settings, and chains to <see cref="FolderPicker"/> to declare one more.
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
        IStudioStrings? strings = null,
        string? initialUiMode = null,
        Action<string>? persistUiMode = null,
        IModelProfileStore? profileStore = null,
        string? teamsRoot = null,
        IShellOpener? shellOpener = null,
        IUiDelay? delay = null,
        string? initialLanguage = null,
        Action<string>? persistLanguage = null,
        Action<string>? applyLanguage = null)
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
            shellOpener,
            delay,
            initialLanguage,
            persistLanguage,
            applyLanguage);
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
                // The team cards' last-run line, from the same history the
                // History screen reads.
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

    /// <summary>The Modify action on a team card — same fault barrier as a resume (W-09).</summary>
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
