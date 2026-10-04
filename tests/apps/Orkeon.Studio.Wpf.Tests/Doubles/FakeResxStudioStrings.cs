using System.Xml.Linq;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// The localization port over one satellite <c>.resx</c> copied next to the tests (<c>Strings.fr.resx</c>…),
/// without the process-wide <c>I18n</c> singleton the shell switches: a key the satellite lacks reads
/// as itself, as <c>I18n</c> renders a missing key.
/// </summary>
public sealed class FakeResxStudioStrings : IStudioStrings
{
    private readonly Dictionary<string, string> _values;

    /// <summary>Reads <c>Resources/Strings.&lt;culture&gt;.resx</c> from the test output.</summary>
    public FakeResxStudioStrings(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", $"Strings.{culture}.resx");
        _values = XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                data => (string?)data.Attribute("name") ?? throw new InvalidOperationException("data without name"),
                data => (string?)data.Element("value") ?? "",
                StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

    /// <inheritdoc />
    public event EventHandler? CultureChanged
    {
        add { }
        remove { }
    }
}
