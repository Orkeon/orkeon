using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>One language the menu can offer: its code, its name in its own language.</summary>
/// <param name="Code">Two-letter code, lowercase — the culture the app switches to.</param>
/// <param name="Display">Uppercase code for the badge («FR»).</param>
/// <param name="Name">The language's own name («Français», «中文»).</param>
public sealed record LanguageOption(string Code, string Display, string Name);

/// <summary>
/// Which language the app speaks, and how that was decided (30/08 mock, T-13/T-14).
/// <para>
/// The decision itself lives here, in a plain class, rather than in the resx shim: this is
/// the half that has rules — the supported set, the fall back to English, and the one that
/// matters most, that a DETECTED language is never persisted. Persisting it would mean a
/// user who changes their Windows language never sees Studio follow, because the first run
/// froze the answer.
/// </para>
/// </summary>
public sealed class LanguageSelectorViewModel : ObservableObject
{
    /// <summary>The languages the catalogue ships. One entry, one satellite resx.</summary>
    public static readonly IReadOnlyList<LanguageOption> Supported =
    [
        new("fr", "FR", "Français"),
        new("en", "EN", "English"),
        new("es", "ES", "Español"),
        new("de", "DE", "Deutsch"),
        new("zh", "ZH", "中文"),
    ];

    private readonly Action<string> _apply;
    private readonly Action<string>? _persist;
    private readonly IStudioStrings _strings;
    private string _current;
    private bool _isExplicit;
    private bool _isMenuOpen;

    /// <summary>
    /// Resolves the starting language and applies it.
    /// </summary>
    /// <param name="storedChoice">
    /// What the user explicitly picked last time, or null. Null is the important case: it
    /// means «nobody chose», so the system decides again on every start.
    /// </param>
    /// <param name="systemLanguage">The OS UI language; defaults to this machine's.</param>
    /// <param name="apply">Switches the running catalogue.</param>
    /// <param name="persist">Records an explicit choice. Never called for a detected one.</param>
    public LanguageSelectorViewModel(
        string? storedChoice = null,
        string? systemLanguage = null,
        Action<string>? apply = null,
        Action<string>? persist = null,
        IStudioStrings? strings = null)
    {
        _apply = apply ?? (_ => { });
        _persist = persist;
        _strings = strings ?? EnglishStudioStrings.Instance;

        if (Normalize(storedChoice) is { } chosen)
        {
            _current = chosen;
            _isExplicit = true;
        }
        else
        {
            _current = Normalize(systemLanguage ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en";
            _isExplicit = false;
        }

        _apply(_current);

        _strings.CultureChanged += (_, _) => OnPropertiesChanged(nameof(MenuHeader), nameof(Tooltip));

        PickCommand = new RelayCommand(code => { if (code is string language) Pick(language); });
        ToggleMenuCommand = new RelayCommand(() => IsMenuOpen = !_isMenuOpen);
        CloseMenuCommand = new RelayCommand(() => IsMenuOpen = false);
    }

    /// <summary>The language in force, two-letter code.</summary>
    public string Current => _current;

    /// <summary>The badge on the button — «FR».</summary>
    public string CurrentDisplay => Option(_current).Display;

    /// <summary>Its name in its own language.</summary>
    public string CurrentName => Option(_current).Name;

    /// <summary>Whether the user picked this language rather than the machine.</summary>
    public bool IsExplicitChoice => _isExplicit;

    /// <summary>
    /// The menu lists the OTHER languages only: the one in force is already on the button,
    /// and offering it again is a row that does nothing.
    /// </summary>
    public IReadOnlyList<LanguageOption> Others =>
        [.. Supported.Where(l => !string.Equals(l.Code, _current, StringComparison.Ordinal))];

    /// <summary>
    /// «SYSTÈME · Français» / «CHOISIE · Français» — the popup's first line. Where the
    /// current language came from is the one thing the badge cannot say, and it decides
    /// whether changing the Windows language will still be followed.
    /// </summary>
    public string MenuHeader => string.Format(
        CultureInfo.CurrentCulture,
        _strings[StudioStringKeys.LanguageHeaderPattern],
        _strings[_isExplicit ? StudioStringKeys.LanguageChosen : StudioStringKeys.LanguageSystem],
        CurrentName);

    /// <summary>«Français — langue du système» / «— choix enregistré», on the button.</summary>
    public string Tooltip => string.Format(
        CultureInfo.CurrentCulture,
        _strings[StudioStringKeys.LanguageTooltipPattern],
        CurrentName,
        _strings[_isExplicit ? StudioStringKeys.LanguageFromChoice : StudioStringKeys.LanguageFromSystem]);

    /// <summary>Whether the popup is showing.</summary>
    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set => SetProperty(ref _isMenuOpen, value);
    }

    /// <summary>Picks a language — and this one IS recorded.</summary>
    public RelayCommand PickCommand { get; }

    /// <summary>Opens and closes the popup.</summary>
    public RelayCommand ToggleMenuCommand { get; }

    /// <summary>Closes it — the click elsewhere, and Échap.</summary>
    public RelayCommand CloseMenuCommand { get; }

    private void Pick(string code)
    {
        IsMenuOpen = false;

        if (Normalize(code) is not { } language || string.Equals(language, _current, StringComparison.Ordinal))
            return;

        _current = language;
        _isExplicit = true;
        _apply(language);
        _persist?.Invoke(language);
        OnPropertiesChanged(
            nameof(Current), nameof(CurrentDisplay), nameof(CurrentName),
            nameof(IsExplicitChoice), nameof(Others), nameof(MenuHeader), nameof(Tooltip));
    }

    private static LanguageOption Option(string code) =>
        Supported.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.Ordinal)) ?? Supported[1];

    private static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        // Compared case-insensitively rather than lowercased: a language tag is an
        // identifier, and CA1308 is right that lowercasing one is a security smell.
        var two = language.Trim();
        if (two.Length > 2)
            two = two[..2];

        return Supported.FirstOrDefault(l => string.Equals(l.Code, two, StringComparison.OrdinalIgnoreCase))?.Code;
    }
}
