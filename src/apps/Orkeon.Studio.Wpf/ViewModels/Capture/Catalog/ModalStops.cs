using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>The scrim modals — four of the six were never captured at all.</summary>
internal static class ModalStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "modale-dossiers-equipe",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Teams,
            Because = "«Les dossiers de cette équipe»: the checkbox rows and their rights badges, "
                    + "over the team list it was opened from.",
            Covers = ["TeamMounts.IsOpen"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.Teams[0].ChangeMountsCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.TeamMounts.CancelCommand.Execute(null)),
        },

        new()
        {
            Name = "modale-dossier-autorise",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            Because = "The chooser that offers the folders the settings already declare — the modal "
                    + "the wizard's «Choisir un dossier…» opens.",
            Covers = ["AllowedFolders.IsOpen"],
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.AllowFolderCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.AllowedFolders.CancelCommand.Execute(null)),
        },

        new()
        {
            Name = "modale-editeur-agent",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            Because = "The agent editor over the Composer: the name, what the agent does, its tool "
                    + "chips and its scope — the one modal that edits a proposal rather than the "
                    + "settings around it.",
            Covers = ["CreateTeam.AgentEditor.IsOpen"],
            Arrange = static async c =>
            {
                // The wizard was restarted by the last step-4 stop, so the proposal has to come
                // back before there is an agent card to edit.
                await c.Shell.CreateTeam.ResumeAsync(
                    Orkeon.Studio.Core.Forge.ForgeSessionCatalog.List(c.World.ForgeWorkspace)
                        .First(session => session.Slug == StudioFixture.DryPauseSessionSlug));
                c.Shell.CreateTeam.Agents[0].EditCommand.Execute(null);
            },
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.AgentEditor.CancelCommand.Execute(null)),
        },

        new()
        {
            Name = "modale-declarer-dossier",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.SettingsFolders,
            Because = "The disk tree, opened from the ONE screen that declares a folder. There is "
                    + "deliberately no way to reach it from the team chooser — «Déclarer un "
                    + "dossier» closes the chooser and sends the user here instead — so the two "
                    + "scrims are never up at once, and this is the picker's only door.",
            Covers = ["FolderPicker.IsOpen"],
            CoversFalse = ["AllowedFolders.IsOpen"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Config.Mounts.AllowFolderCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.FolderPicker.CancelCommand.Execute(null)),
        },
    ];
}
