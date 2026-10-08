using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Orkeon.Hosting;

/// <summary>
/// The sentences the settings catalogue describes a key with: the <c>summary</c> of the property the
/// key is read into, taken from the XML documentation the build writes beside each assembly. One
/// sentence lives in one place — the comment of the code that reads the key.
/// </summary>
internal sealed partial class SettingsDocumentation
{
    private readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);

    /// <summary>A documentation that knows no member: every description is empty.</summary>
    public static SettingsDocumentation Empty { get; } = new();

    /// <summary>The documentation of <paramref name="files"/>, each an XML documentation file.</summary>
    public static SettingsDocumentation From(IEnumerable<XDocument> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var documentation = new SettingsDocumentation();
        foreach (var file in files)
            documentation.Add(file);
        return documentation;
    }

    /// <summary>Adds the members of one XML documentation file (<c>Orkeon.Application.xml</c>).</summary>
    /// <param name="documentation">The file's content.</param>
    public SettingsDocumentation Add(XDocument documentation)
    {
        ArgumentNullException.ThrowIfNull(documentation);
        foreach (var member in documentation.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is { Length: > 0 } name)
                _members[name] = member;
        }

        return this;
    }

    /// <summary>The summary of <paramref name="property"/>, one line of plain text, or empty.</summary>
    public string Of(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);

        // The comment sits on the type that declares the property; an override or an implementation
        // that says <inheritdoc/> sends to the one it inherits.
        for (var type = property.DeclaringType; type is not null; type = type.BaseType)
        {
            if (Summary($"P:{Name(type)}.{property.Name}") is { Length: > 0 } summary)
                return summary;
        }

        foreach (var contract in property.DeclaringType?.GetInterfaces() ?? [])
        {
            if (Summary($"P:{Name(contract)}.{property.Name}") is { Length: > 0 } summary)
                return summary;
        }

        return string.Empty;
    }

    /// <summary>The summary of <paramref name="type"/>, one line of plain text, or empty.</summary>
    public string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Summary($"T:{Name(type)}") ?? string.Empty;
    }

    private string? Summary(string member) =>
        _members.TryGetValue(member, out var element) && element.Element("summary") is { } summary
            ? Text(summary)
            : null;

    /// <summary>The name the compiler gives a type in a documentation file: nested types joined by a dot.</summary>
    private static string Name(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return (definition.FullName ?? definition.Name).Replace('+', '.');
    }

    /// <summary>
    /// A summary as one line: code in backticks, a reference by the short name of what it names, a
    /// paragraph break as a space. A work-item reference a comment carries in brackets — the trace
    /// of why a line was written — is left out: it names nothing a reader of the settings can look up.
    /// </summary>
    private static string Text(XElement summary)
    {
        var text = new StringBuilder();
        Append(text, summary);
        var line = Whitespace().Replace(text.ToString(), " ").Trim();
        return SpaceBeforePunctuation().Replace(WorkItems().Replace(line, string.Empty), "$1");
    }

    private static void Append(StringBuilder text, XElement element)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText plain:
                    text.Append(plain.Value);
                    break;
                case XElement { Name.LocalName: "c" or "code" } code:
                    text.Append('`').Append(code.Value.Trim()).Append('`');
                    break;
                case XElement { Name.LocalName: "see" or "seealso" } reference:
                    text.Append(Reference(reference));
                    break;
                case XElement { Name.LocalName: "paramref" or "typeparamref" } parameter:
                    text.Append('`').Append(parameter.Attribute("name")?.Value).Append('`');
                    break;
                case XElement nested:
                    text.Append(' ');
                    Append(text, nested);
                    text.Append(' ');
                    break;
            }
        }
    }

    private static string Reference(XElement reference)
    {
        if (!string.IsNullOrWhiteSpace(reference.Value))
            return reference.Value;
        if (reference.Attribute("langword")?.Value is { } word)
            return $"`{word}`";
        if (reference.Attribute("href")?.Value is { } address)
            return address;
        if (reference.Attribute("cref")?.Value is not { Length: > 0 } target)
            return string.Empty;

        // "P:Namespace.Type.Member" or "M:Namespace.Type.Method(System.String)": the last two names
        // of a member, the last one of a type.
        var kind = target.Length > 1 && target[1] == ':' ? target[0] : 'T';
        var name = target.Length > 1 && target[1] == ':' ? target[2..] : target;
        var arguments = name.IndexOf('(', StringComparison.Ordinal);
        if (arguments >= 0)
            name = name[..arguments];
        name = Arity().Replace(name, string.Empty);

        var parts = name.Split('.');
        var shown = kind == 'T' || parts.Length < 2 ? parts[^1] : $"{parts[^2]}.{parts[^1]}";
        return $"`{shown}`";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"`+\d+")]
    private static partial Regex Arity();

    /// <summary>
    /// A bracket that holds a work-item reference or a paragraph of a plan — "(GAP-40)",
    /// "(see R2.6 / SEC-009)", "(v2, plan §8.1)" —, whole. Not a standard's or a decision record's
    /// number (AES-256, ADR-012), which a reader can look up.
    /// </summary>
    [GeneratedRegex(@"\s*\((?=[^()]*(?:\b(?!AES-|SHA-|ADR-|RFC-|ISO-|UTF-|TLS-)[A-Z]{2,}-\d+\b|\b(?:plan|guide) §))[^()]*\)")]
    private static partial Regex WorkItems();

    [GeneratedRegex(@"\s+([,.;:])")]
    private static partial Regex SpaceBeforePunctuation();
}
