using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Historique.</summary>
internal static class HistoryStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "historique-vide",
            Category = CaptureCategory.History,
            Screen = CaptureScreen.History,
            World = CaptureWorldKind.Pristine,
            Because = "The hint that stands in for the cards before anything has ever been run.",
            Covers = ["Launch.History.IsEmpty"],
        },

        new()
        {
            Name = "historique-liste",
            Category = CaptureCategory.History,
            Screen = CaptureScreen.History,
            Because = "Seven runs with mixed outcomes: a success, a runtime failure, a cancellation "
                    + "and a script error, each with its duration and token chips.",
            CoversFalse = ["Launch.History.IsEmpty"],
            SweepsLanguages = true,
        },
    ];
}
