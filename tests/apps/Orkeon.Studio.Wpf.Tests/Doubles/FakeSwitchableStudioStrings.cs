using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// The localization port that starts on the English defaults and switches, on demand, to one
/// satellite <c>.resx</c> copied next to the tests, raising <see cref="CultureChanged"/> as the shell does.
/// </summary>
public sealed class FakeSwitchableStudioStrings : IStudioStrings
{
    private readonly FakeResxStudioStrings _satellite;
    private bool _switched;

    /// <summary>Prepares the switch to <c>Resources/Strings.&lt;culture&gt;.resx</c>.</summary>
    public FakeSwitchableStudioStrings(string culture = "fr") => _satellite = new FakeResxStudioStrings(culture, culture);

    /// <inheritdoc />
    public string this[string key] => _switched ? _satellite[key] : EnglishStudioStrings.Instance[key];

    /// <inheritdoc />
    public string Language => _switched ? _satellite.Language : "en";

    /// <inheritdoc />
    public event EventHandler? CultureChanged;

    /// <summary>Moves to the satellite and tells the listeners.</summary>
    public void Switch()
    {
        _switched = true;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
