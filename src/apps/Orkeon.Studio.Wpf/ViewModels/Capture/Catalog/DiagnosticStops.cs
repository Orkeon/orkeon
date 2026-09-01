using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Diagnostic.</summary>
internal static class DiagnosticStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "diagnostic-avec-problemes",
            Category = CaptureCategory.Diagnostic,
            Screen = CaptureScreen.Diagnostic,
            Because = "Six checks, one warning and one failure: the verdict card in the only colour "
                    + "worth reviewing, since a machine where everything is green shows nothing.",
            Covers = ["Config.Diagnostic.HasRun", "Config.Diagnostic.HasIssues"],
            SweepsLanguages = true,
        },

        new()
        {
            Name = "diagnostic-sans-cli",
            Category = CaptureCategory.Diagnostic,
            Screen = CaptureScreen.Diagnostic,
            World = CaptureWorldKind.Pristine,
            Because = "The doctor with no binary to ask: what the screen says when it cannot answer "
                    + "is as much a design as what it says when it can.",
        },
    ];
}
