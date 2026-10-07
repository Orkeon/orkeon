using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Settings and its eight tabs.</summary>
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
                    + "real verdict from a real probe, not a simulated one — and, under them, the "
                    + "read-only « Team folders » section listing the output/ a seeded team keeps "
                    + "inside itself (STUDIO-14).",
            Covers = ["Settings.TeamFolders.HasRows"],
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
                    + "half the screen. The selected row is the writable one, whose long rights "
                    + "label used to stretch the row and hide the name (STUDIO-16): the badge says "
                    + "one word, the closed rights list says the label.",
            Covers = ["Config.Mounts.SelectedMount"],
            Arrange = CaptureAction.Sync(static c =>
                c.Shell.Config.Mounts.SelectedMount = c.Shell.Config.Mounts.Mounts[1]),
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
            Name = "reglages-outils",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsTools,
            Because = "The Tools tab (STUDIO-21): the three tool keys, none remembered on the seeded "
                    + "machine, and the catalogue by family with what web_search, brave_search, "
                    + "image_generation and the database tools need said next to their names.",
            Covers = ["Settings.Tools.HasSecrets"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "reglages-mails",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsEmail,
            Because = "The E-mail tab (STUDIO-67): the line that says which file the accounts are "
                    + "written to, the two seeded accounts and the form of the Gmail one — its rights "
                    + "as six sentences, its sign-in method, and nothing else for the novice; the "
                    + "expert pass adds the servers, the variable names and the section card. Each "
                    + "account carries the dot of its state (STUDIO-69): the Gmail one is ready, and "
                    + "says so above the button that tests its connection.",
            Covers = ["Config.Email.HasAccounts", "Config.Email.HasSelectedAccount"],
            CoversFalse = ["Config.Email.IsAdding", "Config.Email.IsStateOfSavedFile"],
            SweepsLanguages = true,
            // The window reads the states when the tab arrives; a walk without a window asks here.
            Arrange = static c => c.Shell.Config.Email.RefreshStatesAsync(),
        },

        new()
        {
            Name = "reglages-mails-serveurs",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsEmail,
            Modes = CaptureModes.Expert,
            Because = "The custom account in the expert form: no preset fills its servers, so the "
                    + "two hosts are its own and every other field shows what the engine will use as "
                    + "a watermark; it may send, and its allowed recipients are listed one per line. "
                    + "Its password is not stored, so its state is « not ready » with the engine's "
                    + "own sentence, which names the variable (STUDIO-69).",
            Covers = ["Config.Email.IsExpert", "Config.Email.HasSelectedAccount"],
            Arrange = static async c =>
            {
                await c.Shell.Config.Email.RefreshStatesAsync();
                c.Shell.Config.Email.SelectedAccount = c.Shell.Config.Email.Accounts[^1];
            },
            Teardown = CaptureAction.Sync(static c =>
                c.Shell.Config.Email.SelectedAccount = c.Shell.Config.Email.Accounts[0]),
        },

        new()
        {
            Name = "reglages-mcp",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsMcp,
            Modes = CaptureModes.Expert,
            Because = "The MCP tab (STUDIO-21): a stdio server with its command, arguments and "
                    + "environment, and an HTTP server with its URL — the two transports side by "
                    + "side under the switch that connects them.",
            Covers = ["Config.Mcp.HasServers"],
        },

        new()
        {
            Name = "reglages-studio",
            Category = CaptureCategory.Settings,
            Screen = CaptureScreen.SettingsStudio,
            Because = "The Studio tab (STUDIO-35): the provider balance card — the automatic reading, "
                    + "off by default, and one alert threshold per provider whose balance a key reads, "
                    + "the amount the startup read found beside DeepSeek's — with the same balance on "
                    + "the status bar below.",
            Covers = ["Settings.Studio.Thresholds", "StatusBar.Balance.HasBalance"],
            SweepsLanguages = true,
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
