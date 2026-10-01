using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>
/// The create-a-team wizard — the largest gap in the old campaign, which only ever saw a blank
/// step 1 because it ran against whatever the operator's machine held.
/// <para>
/// Steps 2 to 4 are reached by RESUMING a seeded session, not by running an engine: the hydrator
/// rebuilds a whole session from five JSON files, so the proposal, the mounts and the verdict are
/// photographable with no child process anywhere.
/// </para>
/// </summary>
internal static class WizardStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "etape1-vierge",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The wizard's front door: the need field empty, the only thing step 1 asks.",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.HasAssistant"],
            CoversFalse = ["CreateTeam.CanCompose"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
        },

        new()
        {
            Name = "etape1-renseignee",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The need typed — all «Composer» waits for since STUDIO-45 — the state in which it is "
                    + "live, which the empty shot can never show.",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.CanCompose"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c =>
            {
                var wizard = c.Shell.CreateTeam;
                wizard.Need = StudioFixture.Need;
            }),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
        },

        // STUDIO-46 — the Folders step, between the brief and the plan.
        new()
        {
            Name = "dossiers-proposes",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The Folders step: the two folders the need names, read from it by the forge — "
                    + "/inpdf read, still offering «Choisir un dossier…» and «Dans l'équipe», /outmd "
                    + "written, «dans l'équipe : outmd» by default — each name editable, above the "
                    + "conversation, with «Confirmer les dossiers».",
            Covers = ["CreateTeam.IsFoldersStep"],
            CoversFalse = ["CreateTeam.HasFoldersProblem"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                var wizard = c.Shell.CreateTeam;
                wizard.Need = StudioFixture.Need;
                await wizard.ComposeCommand.ExecuteAsync();
            },
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
        },

        // STUDIO-13
        new()
        {
            Name = "etape1-echec-moteur",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "« Composer l'équipe » on a machine whose CLI is gone: the failure card under "
                    + "the status line — the novice sentence, the locator's own text in mono, the "
                    + "copy, retry and diagnostic buttons — and the user still on step 1, knowing why. "
                    + "The owner's recipe (rename orkeon.exe, click) replayed on the seeded machine, "
                    + "through the real locator: no simulated exception anywhere.",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.HasFailure", "CreateTeam.FailureOffersDiagnostic", "CreateTeam.FailureOffersRetry"],
            CoversFalse = ["CreateTeam.IsEngineRunning", "CreateTeam.FailureOffersSettings"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                var wizard = c.Shell.CreateTeam;
                wizard.Need = StudioFixture.Need;

                // The binary goes missing from the machine the pass stands on, and the click is
                // the real gesture: the locator answers NotStarted, the card says so.
                c.World.Machine.IsInstalled = false;
                await wizard.ComposeCommand.ExecuteAsync();
            },
            Teardown = CaptureAction.Sync(static c =>
            {
                c.World.Machine.IsInstalled = true;
                c.Shell.CreateTeam.RestartCommand.Execute(null);
            }),
        },

        // STUDIO-39 — the use cases of step 1: the suggestions under the need (D-03) and the
        // reference a chosen case leaves (D-04). The gallery panel itself is a modal stop.
        new()
        {
            Name = "etape1-cas-proches",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "A need typed and the typing paused: «2 cas proches» under the box. The search "
                    + "session answered three cases; the wizard kept the two that share a word only they "
                    + "carry (concurrents, résumer) and left out the one that shares nothing but «mes».",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.HasCloseUseCases"],
            CoversFalse = ["CreateTeam.HasReferenceUseCase"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.Need = StudioFixture.Need),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.Need = ""),
        },

        new()
        {
            Name = "etape1-inspire-de",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "A case chosen in the gallery: its problem fills the need in the language of the "
                    + "window, and the chip «Inspiré de : Veille concurrentielle» says which case the "
                    + "creation starts from — its ✕ takes the reference away, never the words.",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.HasReferenceUseCase"],
            CoversFalse = ["CreateTeam.Gallery.IsOpen", "CreateTeam.HasCloseUseCases"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                var wizard = c.Shell.CreateTeam;
                await wizard.Gallery.LoadAsync();
                wizard.BrowseUseCasesCommand.Execute(null);
                wizard.Gallery.Cards.First(card => card.Id == "06-competitive-intelligence").ChooseCommand.Execute(null);
            },
            Teardown = CaptureAction.Sync(static c =>
            {
                c.Shell.CreateTeam.RemoveReferenceUseCaseCommand.Execute(null);
                c.Shell.CreateTeam.Need = "";
            }),
        },

        new()
        {
            Name = "etape1-sans-assistant",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            World = CaptureWorldKind.Pristine,
            Because = "The gate a first-run user actually meets: no assistant profile is elected, so "
                    + "the wizard offers to pick one instead of offering to compose.",
            Covers = ["CreateTeam.NeedsAssistant"],
            CoversFalse = ["CreateTeam.HasAssistant"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "etape2-proposition",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The Composer with a real proposal on it: the rationale, the tool chips, the "
                    + "agent cards and the derived mount rows — and, because the session is parked "
                    + "at the dry pause, both «Essayer l'équipe» and «Adopter sans essai».",
            Covers = ["CreateTeam.IsStep2", "CreateTeam.HasProposal", "CreateTeam.CanTryTeam"],
            SweepsLanguages = true,
            Arrange = static async c =>
                await c.Shell.CreateTeam.ResumeAsync(Session(c, StudioFixture.DryPauseSessionSlug)),
        },

        // STUDIO-14 wizard (D-08)
        new()
        {
            Name = "etape2-dossiers-dans-equipe",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The Composer after «Créer tous les dossiers dans l'équipe»: every implied root "
                    + "answered «dans l'équipe : …», the button gone with nothing left to answer, and "
                    + "the input/ warning still standing — an input/ created at adoption is created empty.",
            Covers = ["CreateTeam.IsStep2", "CreateTeam.HasProposal", "CreateTeam.HasTeamMounts", "CreateTeam.NeedsInputFolder"],
            CoversFalse = ["CreateTeam.HasDerivedMounts", "CreateTeam.HasUndeclaredTeamMounts"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                await c.Shell.CreateTeam.ResumeAsync(Session(c, StudioFixture.DryPauseSessionSlug));
                c.Shell.CreateTeam.CreateAllInsideTeamCommand.Execute(null);
            },
        },

        new()
        {
            Name = "etape3-verdict-reussi",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "A trial that passed: the green badge, the score and cost chips, and a "
                    + "checklist whose observations are not failures.",
            Covers = ["CreateTeam.IsStep3", "CreateTeam.HasVerdict", "CreateTeam.VerdictPassing"],
            SweepsLanguages = true,
            Arrange = static async c =>
                await c.Shell.CreateTeam.ResumeAsync(Session(c, StudioFixture.PassingSessionSlug)),
        },

        new()
        {
            Name = "etape3-verdict-echoue",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The other branch, which the passing shot hides entirely: the warning badge, "
                    + "the checklist crosses and the suggestion list.",
            Covers = ["CreateTeam.IsStep3", "CreateTeam.HasVerdict", "CreateTeam.HasSuggestions"],
            CoversFalse = ["CreateTeam.VerdictPassing"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                await c.Shell.CreateTeam.ResumeAsync(Session(c, StudioFixture.FailingSessionSlug));
                c.Shell.CreateTeam.GoStep3Command.Execute(null);
            },
        },

        new()
        {
            Name = "etape2-equipe-rouverte",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "«Modifier» on an adopted team card: the wizard reopens on the Composer with "
                    + "the whole stepper reachable and the adoption fields seeded from the sidecar. "
                    + "It is also the only route to step 4, since a stepper that was never adopted "
                    + "stops at the milestone the session actually reached.",
            Covers = ["CreateTeam.IsStep2", "CreateTeam.HasProposal"],
            Arrange = CaptureAction.Sync(static c => ReopenAdoptedTeam(c)),
        },

        new()
        {
            Name = "etape4-adoption",
            Category = CaptureCategory.Wizard,
            Screen = CaptureScreen.Create,
            Because = "The adoption form: the name, the profile card and the schedule — the last "
                    + "decision the wizard asks for, since the tunnel ends on a blank step 1 (STUDIO-20).",
            Covers = ["CreateTeam.IsStep4"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.GoStep4Command.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
        },
    ];

    /// <summary>
    /// «Modifier» on the adopted team, through the real gesture. The card's command is gated on the
    /// session id the team's <c>forge.json</c> carries, and the wizard asks the engine for the
    /// session (<c>forge reopen</c>, STUDIO-25) — so this stop also proves the seeded record and
    /// the scripted reopen agree on the promoted session.
    /// </summary>
    private static void ReopenAdoptedTeam(CaptureContext context)
    {
        var card = context.Shell.Teams.Teams.First(team => team.ModifyCommand.CanExecute(null));
        card.ModifyCommand.Execute(null);
    }

    /// <summary>The seeded session behind a slug, read back through the real catalogue.</summary>
    private static Orkeon.Studio.Core.Forge.ForgeSolutionSummary Session(CaptureContext context, string slug) =>
        Orkeon.Studio.Core.Forge.ForgeSessionCatalog.List(context.World.ForgeWorkspace)
            .First(session => string.Equals(session.Slug, slug, StringComparison.Ordinal));
}
