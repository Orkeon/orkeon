using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>My teams, full and empty, its archives, its undo banner and its archive suggestion.</summary>
internal static class TeamsStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "equipes-vide",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            World = CaptureWorldKind.Pristine,
            Because = "The empty card a first-run user meets — a whole screen of its own, and one "
                    + "no machine with teams on it can ever show.",
            Covers = ["Teams.IsEmpty"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "equipes-liste",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "Five adopted teams, the most recently active first (STUDIO-32), with their meta "
                    + "lines and last runs, the one whose sidecar names a folder the settings never "
                    + "declared, and the one whose sidecar is a pasted README — its name on one line, "
                    + "its need folded behind a « Voir plus » (STUDIO-16). Above them the search, the "
                    + "order and the « Archives (1) » toggle; no suggestion under sixty days.",
            Covers = ["Teams.HasArchives", "Teams.IsSortedByActivity"],
            CoversFalse = ["Teams.IsEmpty", "Teams.HasArchiveSuggestion"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "equipes-sessions-en-cours",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "«Sessions en cours» under the team list: three creations stopped part-way, "
                    + "each with the step it reached and the two ways out.",
            Covers = ["Teams.HasInProgress"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "equipes-session-suppression",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "An abandoned draft being discarded: the session card's own confirmation, "
                    + "which is a different row from the team card's.",
            Covers = ["Teams.InProgress[0].IsConfirmingDelete"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.InProgress[0].AskDeleteCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.InProgress[0].CancelDeleteCommand.Execute(null)),
        },

        new()
        {
            Name = "equipes-suppression",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "The card's action row REPLACED by its confirmation — not a modal, which is "
                    + "exactly why a screenshot is the only way to review it.",
            Covers = ["Teams.Cards[0].IsConfirmingDelete"],
            CoversFalse = ["Teams.Cards[0].IsIdle"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.Cards[0].AskDeleteCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.Cards[0].CancelDeleteCommand.Execute(null)),
        },

        new()
        {
            Name = "equipes-renommage",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "The card's action row REPLACED by its rename editor (STUDIO-28, D-07), the name "
                    + "ready to be typed over — not a modal, so only a screenshot shows where it sits.",
            Covers = ["Teams.Cards[0].IsRenaming"],
            CoversFalse = ["Teams.Cards[0].IsIdle"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.Cards[0].RenameCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.Cards[0].CancelRenameCommand.Execute(null)),
        },

        new()
        {
            Name = "equipes-archives",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "The Archives view (STUDIO-32, D-01): the archived team alone, its card muted with "
                    + "« Restore » and « Delete » and nothing else, the line that says what an archive "
                    + "keeps, and the toggle lit — a view over the cards, which only a screenshot shows.",
            Covers = ["Teams.ShowArchives", "Teams.Cards[0].IsArchived"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.ToggleArchivesCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.ToggleArchivesCommand.Execute(null)),
            SweepsLanguages = true,
        },

        new()
        {
            Name = "equipes-archivage-annuler",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "« Archive » asks nothing (STUDIO-32, D-02): the card leaves the list and the banner "
                    + "« Team archived — Undo » takes the top of the screen for a few seconds — held here, "
                    + "where it would otherwise be gone before anyone looked.",
            Covers = ["Teams.HasUndo"],
            Arrange = static c => c.Shell.Teams.Cards.Single(card => card.Slug == StudioFixture.StaleTeamSlug)
                .ArchiveCommand.ExecuteAsync(),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.UndoCommand.Execute(null)),
        },

        new()
        {
            Name = "equipes-suggestion",
            Category = CaptureCategory.Teams,
            Screen = CaptureScreen.Teams,
            Because = "The archive suggestion (STUDIO-32, DB-1), as a user who set Settings › Studio to "
                    + "thirty days meets it: the team not launched since, named, with « Archive » and "
                    + "« Not now » — never a scheduled team, never one whose activity is unknown.",
            Covers = ["Teams.HasArchiveSuggestion"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Settings.Studio.SelectedArchiveSuggestion =
                c.Shell.Settings.Studio.ArchiveSuggestionChoices.Single(choice => choice.Days == 30)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Settings.Studio.SelectedArchiveSuggestion =
                c.Shell.Settings.Studio.ArchiveSuggestionChoices.Single(
                    choice => choice.Days == Services.StudioSettings.DefaultArchiveSuggestionDays)),
            SweepsLanguages = true,
        },
    ];
}
