using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
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
        IModelProfileStore? profileStore = null)
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
            strings);

        Settings = new SettingsScreenViewModel(
            Config,
            new ModelProfilesViewModel(profileStore, Config.Llm, strings),
            Mode);

        Forge = new Wpf.ViewModels.Forge.ForgeTabViewModel(
            forgeClient,
            picker,
            dispatcher,
            strings,
            forgeWorkspace);

        // "Relancer" hands the adopted folder to the ordinary launcher — the promoted
        // crew is not proprietary to the Atelier (SPEC §11).
        Forge.RelaunchRequested += (_, e) => Launch.Target.SelectedPath = e.Path;
    }

    /// <summary>The appsettings editor (spec §4).</summary>
    public ConfigTabViewModel Config { get; }

    /// <summary>The unified "Réglages" screen and its model profiles (design v3).</summary>
    public SettingsScreenViewModel Settings { get; }

    /// <summary>The crew launcher (spec §5).</summary>
    public LaunchTabViewModel Launch { get; }

    /// <summary>The Atelier — the "Résoudre" screen (SPEC-ORKEON-FORGE §12).</summary>
    public Wpf.ViewModels.Forge.ForgeTabViewModel Forge { get; }

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
        IModelProfileStore? profileStore = null)
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
                : null);
    }

    /// <summary>Runs the work the window defers until it is shown: locating the CLI, loading the history, reading the model profiles.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            Launch.InitializeAsync(cancellationToken),
            Settings.Profiles.InitializeAsync(cancellationToken));
}
