using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// WPF implementation of the Core localization port (STUDIO-11): resolves
/// <see cref="StudioStringKeys"/> through the resx-backed <see cref="I18n"/>
/// singleton, so strings fabricated below the view layer follow the same
/// hot language switch as the shell. ViewModels subscribe to
/// <see cref="CultureChanged"/> to re-emit their bindings.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase",
    Justification = "I18n is the numeronym for internationalization, kept verbatim from the "
                  + "I18n type this bridges onto; the rule's own suggestion (18NStudioStrings) "
                  + "is not even a legal C# identifier.")]
public sealed class I18nStudioStrings : IStudioStrings, IDisposable
{
    /// <summary>The shared instance bridging onto <see cref="I18n.Instance"/>.</summary>
    public static I18nStudioStrings Instance { get; } = new();

    private I18nStudioStrings()
    {
        I18n.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <inheritdoc />
    public string this[string key] => I18n.Instance[key];

    /// <inheritdoc />
    public event EventHandler? CultureChanged;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
        => CultureChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public void Dispose() => I18n.Instance.PropertyChanged -= OnLanguageChanged;
}
