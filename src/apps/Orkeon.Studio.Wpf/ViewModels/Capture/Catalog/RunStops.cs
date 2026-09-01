using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Exécuter.</summary>
internal static class RunStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "executer-sans-cible",
            Category = CaptureCategory.Run,
            Screen = CaptureScreen.Run,
            World = CaptureWorldKind.Pristine,
            Because = "No team chosen and no CLI installed: the rescue row and the missing-binary "
                    + "banner at once, which is exactly what a first run looks like.",
            Covers = ["Launch.CliBanner"],
            CoversFalse = ["Launch.HasTeamCard", "Launch.IsBinaryAvailable"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "executer-carte-equipe",
            Category = CaptureCategory.Run,
            Screen = CaptureScreen.Run,
            Because = "A team card on the launcher: the sidecar-backed name, the meta line and the "
                    + "progress card waiting to be started.",
            Covers = ["Launch.HasTeamCard"],
            SweepsLanguages = true,
            Arrange = CaptureAction.Sync(static c =>
                c.Shell.Launch.Target.Select(c.World.TeamDirectory("veille-concurrentielle"))),
        },

        new()
        {
            Name = "executer-dossiers-non-declares",
            Category = CaptureCategory.Run,
            Screen = CaptureScreen.Run,
            Because = "The team whose sidecar names a folder nobody declared: the red banner and "
                    + "the refusal to launch, straight out of the seeded data.",
            Covers = ["Launch.HasTeamCard", "Launch.IsBlockedByUndeclaredFolders"],
            Arrange = CaptureAction.Sync(static c =>
                c.Shell.Launch.Target.Select(c.World.TeamDirectory(StudioFixture.BlockedTeamSlug))),
        },
    ];
}
