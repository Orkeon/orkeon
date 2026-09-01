namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>Importer.</summary>
internal static class ImportStops
{
    /// <summary>The stops.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        new()
        {
            Name = "importer-vide",
            Category = CaptureCategory.Import,
            Screen = CaptureScreen.Import,
            Because = "The drop zone before anything is chosen: the screen as it is nine times out of ten.",
            SweepsLanguages = true,
        },
    ];
}
