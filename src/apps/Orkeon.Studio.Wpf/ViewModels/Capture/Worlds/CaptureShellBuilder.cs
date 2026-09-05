using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// Builds the window's ViewModel over a seeded world.
/// <para>
/// Every store here is the PRODUCTION one, pointed at a path of the world: the physical settings
/// store, the physical directory and target probes, the file-backed history and profile stores,
/// <c>TeamCatalog</c> over the seeded teams root, <c>ForgeSessionCatalog</c> over the seeded
/// workspace. That is deliberate — screenshots taken over in-memory doubles would prove the doubles
/// render, and screenshots over the real loaders prove the application does.
/// </para>
/// </summary>
internal static class CaptureShellBuilder
{
    /// <summary>The shell for <paramref name="world"/>, in the appearance <paramref name="host"/> names.</summary>
    public static MainWindowViewModel Build(CaptureWorld world, CaptureHost host)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(host);

        return new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = PhysicalAppSettingsStore.Instance,
                Directories = PhysicalDirectoryProbe.Instance,
                TargetProbe = PhysicalTargetProbe.Instance,
                // A picker that always cancels: no dialog can ever block a run with nobody at the keyboard.
                Picker = NullPathPicker.Instance,
                ProcessRunner = world.Runner,
                HistoryStore = world.HistoryStore,
                Dispatcher = host.Dispatcher,
                Strings = host.Strings,
                ForgeClient = new Orkeon.Studio.Core.Forge.ForgeClient(world.Cli, world.Locator),
                ProfileStore = world.ProfileStore,
                ShellOpener = NullShellOpener.Instance,
                Delay = world.Delay,
                LlmProbe = world.LlmProbe,
                KeyStore = world.KeyStore,
            },
            new StudioUiPreferences
            {
                InitialMode = host.Mode,
                InitialLanguage = host.Language,
                ApplyLanguage = host.ApplyLanguage,
                SystemLanguage = host.Language,
                // PersistMode and PersistLanguage are left unset, and that is load-bearing: the
                // campaign toggles mode and language constantly, and having nowhere to write them
                // is the whole reason it cannot rewrite the operator's stored preferences.
            },
            globalPathOverride: world.SettingsPath,
            forgeWorkspace: world.ForgeWorkspace,
            teamsRoot: world.TeamsRoot);
    }

    /// <summary>
    /// The work a shell needs before it can be photographed: its own deferred loaders, and the
    /// settings document.
    /// <para>
    /// The settings have to be asked for. Nothing in Studio loads them at startup — the shell
    /// initialises the launcher, the profiles, the doctor and the team list, and not the
    /// configuration — yet the declared folders are read LIVE from it everywhere: the wizard's
    /// mount rows, the team cards' chips, the folder chooser, and the refusal to launch a team
    /// whose folders nobody declared. Left unloaded, every screen of the collection would show a
    /// machine that has declared nothing, and every team would read as blocked.
    /// </para>
    /// </summary>
    public static async Task PrepareAsync(MainWindowViewModel shell, CaptureWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(world);

        await shell.InitializeAsync(cancellationToken);
        await shell.Config.LoadAsync(world.SettingsPath, cancellationToken);
    }
}
