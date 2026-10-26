namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>The Launch screen in trial mode (the former Tester screen, merged in refonte lot 4).</summary>
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
            Because = "The Lancer screen switched to Essai with no team picked: the switch, the trial "
                    + "notice and the team picker of the empty state.",
        },
    ];
}
