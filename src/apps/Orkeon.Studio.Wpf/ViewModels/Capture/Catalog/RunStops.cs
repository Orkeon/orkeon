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

        new()
        {
            Name = "executer-en-cours",
            Category = CaptureCategory.Run,
            Screen = CaptureScreen.Run,
            Because = "A run in flight — the plain-language progress card moving, the tone badge, "
                    + "the token meter and the technical journal filling. It exists only while a "
                    + "stream is open, so no artefact on disk can reproduce it: the scripted CLI is "
                    + "held open across this shot and released by the next one.",
            Covers = ["Launch.IsRunning", "Launch.HasTeamCard"],
            Arrange = static async c =>
            {
                c.Shell.Launch.Target.Select(c.World.TeamDirectory("veille-concurrentielle"));
                c.World.Cli.Hold("run");
                c.HoldUntilTeardown(c.Shell.Launch.RunCommand.ExecuteAsync());
                await CaptureWait.UntilAsync(() => c.Shell.Launch.IsRunning);
            },
            // The stop owns its whole run: a held child released by the NEXT stop would leave the
            // walk waiting on a task nothing in this stop can finish.
            Teardown = static async c =>
            {
                c.World.Cli.Release();
                await CaptureWait.UntilAsync(() => !c.Shell.Launch.IsRunning);
            },
        },

        new()
        {
            Name = "executer-termine",
            Category = CaptureCategory.Run,
            Screen = CaptureScreen.Run,
            Because = "The same run, finished: the badge in its success tone, the «ouvrir le "
                    + "résultat» action live, and the journal holding what the stream said.",
            CoversFalse = ["Launch.IsRunning"],
            SweepsLanguages = true,
            Arrange = static async c =>
            {
                c.Shell.Launch.Target.Select(c.World.TeamDirectory("veille-concurrentielle"));
                await c.Shell.Launch.RunCommand.ExecuteAsync();
            },
        },
    ];
}
