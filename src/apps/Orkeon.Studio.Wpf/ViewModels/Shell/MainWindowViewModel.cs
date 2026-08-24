using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.ViewModels.Config;
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
        // The forge workspace defaults to the Orkeon user home (~/Orkeon), never to the
        // process working directory: launched from the installed app or a dev tree, that
        // directory is the executable's bin folder — sessions would land in bin/.orkeon
        // and vanish on the next clean, and the assistant's /workspace would show DLLs.
        var forgeHome = TeamCatalog.EnsureDirectory(forgeWorkspace
            ?? System.IO.Path.GetDirectoryName(teamsHome)
            ?? teamsHome);
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

        Teams = new TeamsViewModel(teamsRoot, forgeHome, strings: strings, shellOpener: shellOpener);

        // The expert trial screen runs over its own launcher, with NO history store: a
        // trial is a rehearsal, not a run to replay from the history.
        Test = new TestTeamViewModel(
            new LaunchTabViewModel(runner, targetProbe, directories, picker, null, settingsStore, dispatcher, strings, TeamEnvironment, shellOpener),
            teamsRoot);

        Import = new ImportTeamViewModel(targetProbe, picker, strings, teamsRoot);

        // An adopted team is an ordinary folder: "Lancer" hands it to the launcher, the
        // adoption or an import refreshes the lists, a stopped session resumes in the wizard.
        Teams.LaunchRequested += (_, e) => Launch.Target.Select(e.Path);
        Teams.ResumeRequested += (_, e) => _ = CreateTeam.ResumeAsync(e.Session);
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
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            Launch.InitializeAsync(cancellationToken),
            Test.Launcher.InitializeAsync(cancellationToken),
            Settings.Profiles.InitializeAsync(cancellationToken),
            // The silent doctor run (audit 09/20): the sidebar dot and the verdict card
            // are honest from the first frame, without the user pressing anything.
            Config.Diagnostic.InitializeAsync(cancellationToken));

    /// <summary>
    /// The environment an adopted team lays over its launches: the sidecar names a model
    /// profile, the profile store resolves it to <c>ORKEON_Llm__*</c> overrides. Null for a
    /// target that is not a team or names no (or an unknown) profile — the launch then runs
    /// on the settings file, like any other.
    /// </summary>
    private IReadOnlyDictionary<string, string>? TeamEnvironment(string targetPath) =>
        Settings.Profiles.Set.Find(TeamCatalog.ProfileFor(targetPath))?.EnvironmentOverrides(Environment.GetEnvironmentVariable);
}
