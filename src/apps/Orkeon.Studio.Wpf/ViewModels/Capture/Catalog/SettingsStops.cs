using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Réglages and its four tabs.</summary>
internal static class SettingsStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "reglages-modele-vide",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsModel,
            World = CaptureWorldKind.Pristine,
            Because = "No profile at all: the placeholder over the assistant combo and the orange "
                    + "glyph beside it — the state that sends a first-run user to the editor.",
            Covers = ["Settings.Profiles.IsEmpty"],
            CoversFalse = ["Settings.Profiles.HasStudioProfile"],
        },

        new()
        {
            Name = "reglages-modele",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsModel,
            Because = "Four profiles, one elected as the assistant's, and an API-keys card with a "
                    + "cloud profile whose key was never stored.",
            Covers = ["Settings.Profiles.HasStudioProfile", "Settings.Profiles.HasSecrets"],
            CoversFalse = ["Settings.Profiles.IsEmpty"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "reglages-dossiers",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsFolders,
            Because = "Four declared folders, one of which does not exist on disk — the red row is a "
                    + "real verdict from a real probe, not a simulated one.",
            SweepsLanguages = true,
        },

        new()
        {
            Name = "reglages-dossiers-selection",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsFolders,
            Modes = CaptureModes.Expert,
            Because = "The expert mount editor with a row selected: the right pane and its "
                    + "separator only exist once something is picked, so the unselected shot shows "
                    + "half the screen.",
            Covers = ["Config.Mounts.SelectedMount"],
            Arrange = CaptureAction.Sync(static c =>
                c.Shell.Config.Mounts.SelectedMount = c.Shell.Config.Mounts.Mounts[0]),
            Teardown = CaptureAction.Sync(static c => c.Shell.Config.Mounts.SelectedMount = null),
        },

        new()
        {
            Name = "reglages-limites",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsLimits,
            Modes = CaptureModes.Expert,
            Because = "The four limit cards, two of them present in the settings and two falling "
                    + "back to their defaults — both faces in one shot.",
        },

        new()
        {
            Name = "reglages-json",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsJson,
            Modes = CaptureModes.Expert,
            Because = "The raw file, its save location and the resolution chain — the expert's way "
                    + "of checking what Studio actually read.",
        },

        new()
        {
            Name = "reglage-editeur",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsModel,
            Because = "The profile editor over its scrim, on a cloud provider card: the URL and "
                    + "model fields, the key block and the test-connection button.",
            Covers = ["Settings.Profiles.IsEditorOpen"],
            Arrange = CaptureAction.Sync(static c =>
            {
                var profiles = c.Shell.Settings.Profiles;
                profiles.NewProfileCommand.Execute(null);
                if (profiles.Editor is { } editor)
                    editor.SelectedProvider = editor.Providers.FirstOrDefault(p => p.Name == LlmPresets.DeepSeek);
            }),
            Teardown = CaptureAction.Sync(static c =>
                c.Shell.Settings.Profiles.Editor?.CancelCommand.Execute(null)),
        },
    ];

}
