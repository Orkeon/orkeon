using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>My teams, full and empty.</summary>
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
            Because = "Three adopted teams with their meta lines, their last-run chips, and the one "
                    + "whose sidecar names a folder the settings never declared.",
            CoversFalse = ["Teams.IsEmpty"],
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
            Covers = ["Teams.Teams[0].IsConfirmingDelete"],
            CoversFalse = ["Teams.Teams[0].IsIdle"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Teams.Teams[0].AskDeleteCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Teams.Teams[0].CancelDeleteCommand.Execute(null)),
        },
    ];
}
