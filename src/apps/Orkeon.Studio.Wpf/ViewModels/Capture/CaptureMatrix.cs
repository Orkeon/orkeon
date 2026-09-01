using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>
/// Which passes the campaign runs.
/// <para>
/// Mode × theme is the real fidelity matrix: a dark-theme regression is a token bug, invisible in
/// light and present on EVERY screen, so both themes walk the whole catalogue. Language is a
/// different question — whether the text fits — and it needs the screens with the longest strings,
/// not the same screen four more times. Hence a sweep over a tagged subset rather than a fifth
/// dimension of the cross product, which would be some 1600 images nobody would read.
/// </para>
/// </summary>
internal sealed record CaptureMatrix
{
    /// <summary>
    /// The language the main matrix is captured in. Pinned rather than read from the machine, so
    /// the collection is the same on any operator's box.
    /// </summary>
    public string BaseLanguage { get; init; } = "fr";

    /// <summary>The modes the whole catalogue is walked in.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [UiModeViewModel.Novice, UiModeViewModel.Expert];

    /// <summary>The themes the whole catalogue is walked in.</summary>
    public IReadOnlyList<bool> Themes { get; init; } = [false, true];

    /// <summary>
    /// The languages the tagged subset is repeated in — read from the supported list, so a sixth
    /// language sweeps itself instead of being silently absent.
    /// </summary>
    public IReadOnlyList<string> SweepLanguages { get; init; } =
        [.. LanguageSelectorViewModel.Supported
            .Select(language => language.Code)
            .Where(code => !string.Equals(code, "fr", StringComparison.Ordinal))];

    /// <summary>The default matrix.</summary>
    public static CaptureMatrix Default { get; } = new();

    /// <summary>
    /// The passes, in order. Theme is outermost because applying it swaps a merged dictionary —
    /// a global — while a mode switch is a ViewModel toggle.
    /// </summary>
    public IReadOnlyList<CaptureAppearance> Passes =>
    [
        .. Themes.SelectMany(dark => Modes.Select(mode => new CaptureAppearance(BaseLanguage, dark, mode))),
        .. SweepLanguages.Select(language => new CaptureAppearance(language, false, UiModeViewModel.Novice)),
    ];

    /// <summary>True when a pass is one of the language-sweep passes.</summary>
    public bool IsSweep(CaptureAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return !string.Equals(appearance.Language, BaseLanguage, StringComparison.Ordinal);
    }
}
