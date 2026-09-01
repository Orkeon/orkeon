namespace Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

/// <summary>
/// Every shot the campaign takes, in the order it takes them.
/// <para>
/// Order is load-bearing: a wizard stop is reachable from the state the previous one left, and the
/// headless walkthrough replays the whole list in this order precisely so that a stop leaking state
/// into the next one fails by name.
/// </para>
/// </summary>
internal static class CaptureCatalog
{
    /// <summary>The whole catalogue.</summary>
    public static IReadOnlyList<CaptureStop> All { get; } =
    [
        .. OnboardingStops.All,
        .. WizardStops.All,
        .. TeamsStops.All,
        .. ImportStops.All,
        .. TestScreenStops.All,
        .. RunStops.All,
        .. HistoryStops.All,
        .. SettingsStops.All,
        .. DiagnosticStops.All,
        .. ModalStops.All,
        .. AssistantStops.All,
    ];

    /// <summary>The stops that belong to one pass.</summary>
    public static IReadOnlyList<CaptureStop> For(CaptureAppearance appearance, CaptureMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(matrix);

        var sweep = matrix.IsSweep(appearance);

        return [.. All.Where(stop => Applies(stop, appearance.ModeFlag) && (!sweep || stop.SweepsLanguages))];
    }

    /// <summary>
    /// Whether a stop belongs to a mode pass. <see cref="CaptureModes.Either"/> is captured once,
    /// in the novice pass: the shot does not depend on the mode, and taking it twice would put two
    /// identical images in the collection.
    /// </summary>
    public static bool Applies(CaptureStop stop, CaptureModes mode)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return stop.Modes == CaptureModes.Either
            ? mode == CaptureModes.Novice
            : stop.Modes.HasFlag(mode);
    }
}
