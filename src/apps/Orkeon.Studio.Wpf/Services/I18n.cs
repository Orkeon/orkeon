using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// Runtime-switchable localization over Resources/Strings.resx and its four satellites
/// (fr, es, de, zh-Hans).
/// <para>
/// A shim, deliberately: which language to start in, and whether that was a choice or a
/// detection, are decisions with rules, and they live in
/// <c>ViewModels/Shell/LanguageSelectorViewModel</c> where the test suite can reach them.
/// All this does is resolve a key in a culture and tell the bindings when the culture moved.
/// </para>
/// XAML: Text="{Binding [Studio.Shell.Create], Source={x:Static services:I18n.Instance}}"
/// C#:   I18n.T("Studio.Shell.TourSkip");  I18n.Instance.SetLanguage("es");
/// </summary>
public sealed class I18n : INotifyPropertyChanged
{
    /// <summary>The shared instance.</summary>
    public static I18n Instance { get; } = new();

    private readonly ResourceManager _rm =
        new("Orkeon.Studio.Wpf.Resources.Strings", typeof(I18n).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    private I18n() { }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The two-letter code in force.</summary>
    public string Language => _culture.TwoLetterISOLanguageName;

    /// <summary>Resolves a key; a miss returns the key, which is how a gap becomes visible.</summary>
    public string this[string key] => _rm.GetString(key, _culture) ?? key;

    /// <summary>Resolves a key on the shared instance.</summary>
    public static string T(string key) => Instance[key];

    /// <summary>
    /// Switches the running catalogue. Chinese is asked for as "zh" and resolved as
    /// "zh-Hans": the satellite is written in simplified characters, and a bare "zh" would
    /// not find it.
    /// </summary>
    public void SetLanguage(string lang)
    {
        var culture = CultureInfo.GetCultureInfo(
            string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase) ? "zh-Hans" : lang);

        if (Equals(culture, _culture))
            return;

        _culture = culture;
        // "Item[]" re-evaluates every indexer binding in one shot.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }
}
