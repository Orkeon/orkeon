namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>The expert trial screen.</summary>
internal static class TestScreenStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "tester-sans-equipe",
            Category = CaptureCategory.TestScreen,
            Screen = CaptureScreen.Test,
            Modes = CaptureModes.Expert,
            Because = "The two-column trial layout with its full-height console empty — the state "
                    + "that shows whether the console keeps its height when it has nothing in it.",
        },
    ];
}
