using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// Runtime-switchable localization over Resources/Strings.resx (+ Strings.fr.resx).
/// XAML: Text="{Binding [Nav_Start], Source={x:Static services:I18n.Instance}}"
/// C#:   I18n.T("Tour_Skip");  I18n.Instance.SetLanguage("fr");
/// </summary>
public sealed class I18n : INotifyPropertyChanged
{
    public static I18n Instance { get; } = new();

    private readonly ResourceManager _rm =
        new("Orkeon.Studio.Wpf.Resources.Strings", typeof(I18n).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    private I18n() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"en" or "fr".</summary>
    public string Language => _culture.TwoLetterISOLanguageName;

    public string this[string key] => _rm.GetString(key, _culture) ?? key;

    public static string T(string key) => Instance[key];

    public void SetLanguage(string lang)
    {
        var culture = CultureInfo.GetCultureInfo(lang);
        if (Equals(culture, _culture))
            return;

        _culture = culture;
        // "Item[]" re-evaluates every indexer binding in one shot.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }
}
