using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>The plate, the tour, the language menu and the About panel.</summary>
internal static class OnboardingStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "demarrage",
            Category = CaptureCategory.Onboarding,
            Screen = CaptureScreen.Create,
            Modes = CaptureModes.Either,
            Because = "The startup plate is a screen of the design too, and the one a user meets first.",
            Arrange = CaptureAction.Sync(static c => c.Surface.ShowSplash()),
            Teardown = CaptureAction.Sync(static c => c.Surface.HideSplash()),
        },

        // Projected from the catalogue the window reads, so a sixth tour stop arrives in the
        // collection on its own instead of being silently missing from it.
        .. GuidedTourCatalog.Steps.Select((step, index) => new CaptureStop
        {
            Name = $"visite-etape{index + 1}",
            Category = CaptureCategory.Onboarding,
            Screen = CaptureScreen.Create,
            Modes = CaptureModes.Either,
            Because = step.TargetName is { } target
                ? $"Guided-tour stop {index + 1}: the spotlight on {target}."
                : $"Guided-tour stop {index + 1}: the closing card, with no spotlight.",
            Arrange = CaptureAction.Sync(c => c.Surface.StartTourAt(index)),
            Teardown = CaptureAction.Sync(static c => c.Surface.EndTour()),
        }),

        new()
        {
            Name = "menu-langue",
            Category = CaptureCategory.Onboarding,
            Screen = CaptureScreen.Create,
            Modes = CaptureModes.Either,
            Because = "The only Popup in the app, and therefore the only shot that needs the window "
                    + "and its own HwndSource composited — a plain render would show the rotated "
                    + "chevron above nothing.",
            Covers = ["Language.IsMenuOpen"],
            Arrange = CaptureAction.Sync(static c => c.Shell.Language.ToggleMenuCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.Language.CloseMenuCommand.Execute(null)),
        },

        new()
        {
            Name = "a-propos",
            Category = CaptureCategory.Onboarding,
            Screen = CaptureScreen.Create,
            Because = "The About panel over a scrim: the version line, the plate and the way back "
                    + "into the guided tour.",
            Covers = ["About.IsOpen"],
            Arrange = CaptureAction.Sync(static c => c.Shell.About.OpenCommand.Execute(null)),
            Teardown = CaptureAction.Sync(static c => c.Shell.About.CloseCommand.Execute(null)),
        },
    ];
}
