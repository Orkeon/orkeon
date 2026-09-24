using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>The scrim modals — four of the first five were never captured before the campaign.</summary>
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

        // STUDIO-39 — the use-case gallery, the side panel step 1 opens (D-02 to D-06).
        new()
        {
            Name = "modale-galerie-cas-usage",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            Because = "The use-case gallery over step 1: the search, the category and process chips, the "
                    + "two needs of the machine as filters, and one card per case — title, problem, how the "
                    + "team works — with «Référence seule» on the finance cases, which can inspire a team "
                    + "but not be imported as they are.",
            Covers = ["CreateTeam.Gallery.IsOpen", "CreateTeam.Gallery.HasCards"],
            CoversFalse = ["CreateTeam.Gallery.HasFailure"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                await c.Shell.CreateTeam.Gallery.LoadAsync();
                c.Shell.CreateTeam.BrowseUseCasesCommand.Execute(null);
            },
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.Gallery.CloseCommand.Execute(null)),
        },

        new()
        {
            Name = "modale-galerie-finance",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            Because = "The gallery on one category: its chip lit, the count down to the finance cases, and "
                    + "both marked reference only — still browsable, and still a reference a creation can "
                    + "start from (D-06).",
            Covers = ["CreateTeam.Gallery.IsOpen", "CreateTeam.Gallery.HasCards"],
            Arrange = static async c =>
            {
                var gallery = c.Shell.CreateTeam.Gallery;
                await gallery.LoadAsync();
                c.Shell.CreateTeam.BrowseUseCasesCommand.Execute(null);
                gallery.Categories.First(chip => chip.Key == "03-finance-trading").SelectCommand.Execute(null);
            },
            Teardown = CaptureAction.Sync(static c =>
            {
                c.Shell.CreateTeam.Gallery.ClearFiltersCommand.Execute(null);
                c.Shell.CreateTeam.Gallery.CloseCommand.Execute(null);
            }),
        },

        new()
        {
            Name = "modale-galerie-cas-proches",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            Because = "«2 cas proches» clicked under the need: the gallery opens on the cases close to it, "
                    + "best first, their filter ticked — the way from a suggestion to the card that fills "
                    + "the need.",
            Covers = ["CreateTeam.Gallery.IsOpen", "CreateTeam.Gallery.SuggestedOnly", "CreateTeam.HasCloseUseCases"],
            Arrange = static async c =>
            {
                var wizard = c.Shell.CreateTeam;
                await wizard.Gallery.LoadAsync();
                wizard.Need = StudioFixture.Need;
                wizard.ShowCloseUseCasesCommand.Execute(null);
            },
            Teardown = CaptureAction.Sync(static c =>
            {
                c.Shell.CreateTeam.Gallery.ClearFiltersCommand.Execute(null);
                c.Shell.CreateTeam.Gallery.CloseCommand.Execute(null);
                c.Shell.CreateTeam.Need = "";
            }),
        },

        new()
        {
            Name = "modale-galerie-sans-moteur",
            Category = CaptureCategory.Modal,
            Screen = CaptureScreen.Create,
            World = CaptureWorldKind.Pristine,
            Because = "The gallery on the machine without a CLI: no catalogue made up on Studio's side "
                    + "(D-05), the wizard's own «moteur introuvable» card instead — the locator's words, "
                    + "a retry, and the way to the diagnostic.",
            Covers = ["CreateTeam.Gallery.IsOpen", "CreateTeam.Gallery.IsEngineMissing"],
            CoversFalse = ["CreateTeam.Gallery.HasCards"],
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.BrowseUseCasesCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.Gallery.CloseCommand.Execute(null)),
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
    ];
}
