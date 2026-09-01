using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>One pass of the campaign: a language, a theme and a mode.</summary>
/// <param name="Language">Language code, as <see cref="LanguageSelectorViewModel.Supported"/> spells it.</param>
/// <param name="IsDark">Whether the dark tokens are in force.</param>
/// <param name="Mode">Novice or expert.</param>
internal sealed record CaptureAppearance(string Language, bool IsDark, string Mode)
{
    /// <summary>The theme's folder name.</summary>
    public string ThemeName => IsDark ? "dark" : "light";

    /// <summary>Which mode flag this pass captures.</summary>
    public CaptureModes ModeFlag =>
        string.Equals(Mode, UiModeViewModel.Expert, StringComparison.Ordinal)
            ? CaptureModes.Expert
            : CaptureModes.Novice;

    /// <summary>The directory this pass writes into, relative to the campaign's root.</summary>
    public string Folder => $"{Language}/{ThemeName}/{Mode}";
}
