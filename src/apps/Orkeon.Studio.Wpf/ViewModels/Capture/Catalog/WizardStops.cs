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
            Because = "The wizard's front door: the need field empty and the three chip rows untouched.",
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
            Because = "Every chip row decided and the need typed — the only state in which «Composer» "
                    + "is live, which the empty shot can never show.",
            Covers = ["CreateTeam.IsStep1", "CreateTeam.CanCompose"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c =>
            {
                var wizard = c.Shell.CreateTeam;
                wizard.Need = StudioFixture.Need;
                wizard.Outcome = StudioFixture.Outcome;
                wizard.FrequencyChoices[1].SelectCommand.Execute(null);
                wizard.SourceChoices[0].SelectCommand.Execute(null);
                wizard.OutputChoices[0].SelectCommand.Execute(null);
            }),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
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
            Because = "The adoption form before anything is saved: the name, the profile card and "
                    + "the schedule — the last decision the wizard asks for.",
            Covers = ["CreateTeam.IsStep4", "CreateTeam.NotSaved"],
            CoversFalse = ["CreateTeam.IsSaved"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c => c.Shell.CreateTeam.GoStep4Command.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.CreateTeam.RestartCommand.Execute(null)),
        },
    ];

    /// <summary>
    /// «Modifier» on the adopted team, through the real gesture. The card's command is gated on a
    /// session pointing back at the folder, so this stop also proves the seeded <c>promotedTo</c>
    /// is the one the reverse lookup accepts.
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
