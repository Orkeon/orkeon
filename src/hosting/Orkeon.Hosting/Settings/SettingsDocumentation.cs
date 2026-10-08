using System.Globalization;
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
            if (Summary($"P:{Name(type)}.{property.Name}", property) is { Length: > 0 } summary)
                return summary;
        }

        foreach (var contract in property.DeclaringType?.GetInterfaces() ?? [])
        {
            if (Summary($"P:{Name(contract)}.{property.Name}", property) is { Length: > 0 } summary)
                return summary;
        }

        return string.Empty;
    }

    /// <summary>The summary of <paramref name="type"/>, one line of plain text, or empty.</summary>
    public string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Summary($"T:{Name(type)}", home: null) ?? string.Empty;
    }

    private string? Summary(string member, PropertyInfo? home) =>
        _members.TryGetValue(member, out var element) && element.Element("summary") is { } summary
            ? Text(summary, home)
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
    /// So is the "Gets or sets" a property's comment opens with: a key is not got nor set, it is.
    /// </summary>
    /// <param name="summary">The comment's <c>summary</c> element.</param>
    /// <param name="home">The property the comment is on, which says how a reference reads; null for a type's.</param>
    private static string Text(XElement summary, PropertyInfo? home)
    {
        var text = new StringBuilder();
        Append(text, summary, home);
        var line = Whitespace().Replace(text.ToString(), " ").Trim();
        line = SpaceBeforePunctuation().Replace(WorkItems().Replace(line, string.Empty), "$1");

        var accessor = Accessor().Match(line);
        return accessor.Success && accessor.Length < line.Length
            ? char.ToUpperInvariant(line[accessor.Length]) + line[(accessor.Length + 1)..]
            : line;
    }

    private static void Append(StringBuilder text, XElement element, PropertyInfo? home)
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
                    text.Append(Reference(reference, home));
                    break;
                case XElement { Name.LocalName: "paramref" or "typeparamref" } parameter:
                    text.Append('`').Append(parameter.Attribute("name")?.Value).Append('`');
                    break;
                case XElement nested:
                    text.Append(' ');
                    Append(text, nested, home);
                    text.Append(' ');
                    break;
            }
        }
    }

    private static string Reference(XElement reference, PropertyInfo? home)
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
        if (kind == 'T' || parts.Length < 2)
            return $"`{parts[^1]}`";

        return $"`{Member(kind, name[..name.LastIndexOf('.')], parts[^1], home) ?? $"{parts[^2]}.{parts[^1]}"}`";
    }

    /// <summary>
    /// How a reference to a member reads beside the key it is written on, when the settings say it
    /// better than <c>Type.Member</c>: a constant by its value (<c>Defaults to `3`</c>), a value of
    /// an enumeration by its name — what a settings file writes —, and a property of the options the
    /// key belongs to by its own name, since it is the key next to it. Null for any other member.
    /// </summary>
    private static string? Member(char kind, string typeName, string member, PropertyInfo? home)
    {
        if (home?.DeclaringType is not { } declaring)
            return null;

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var type = Named(typeName, home);
        switch (kind)
        {
            case 'F' when type is { IsEnum: true }:
                return member;
            case 'F' when type?.GetField(member, Any) is { IsLiteral: true } constant:
                return Convert.ToString(constant.GetRawConstantValue(), CultureInfo.InvariantCulture);
            case 'P' when type is not null && type.IsAssignableFrom(declaring):
                return member;
            default:
                return null;
        }
    }

    /// <summary>
    /// The type a reference names, looked for where a comment of <paramref name="home"/> can reach
    /// without a search of the whole process: the options themselves and the types they inherit, the
    /// type of the key, and the assembly that declares them.
    /// </summary>
    private static Type? Named(string typeName, PropertyInfo home)
    {
        if (home.DeclaringType is not { } declaring)
            return null;

        for (Type? type = declaring; type is not null; type = type.BaseType)
        {
            if (string.Equals(Name(type), typeName, StringComparison.Ordinal))
                return type;
        }

        var value = Nullable.GetUnderlyingType(home.PropertyType) ?? home.PropertyType;
        if (string.Equals(Name(value), typeName, StringComparison.Ordinal))
            return value;

        Type?[] declared;
        try
        {
            declared = declaring.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            declared = ex.Types;
        }

        if (declared.FirstOrDefault(type => type is not null && string.Equals(Name(type), typeName, StringComparison.Ordinal)) is { } local)
            return local;

        // A constant kept in another Orkeon assembly the options reference (the defaults of a subsystem).
        foreach (var reference in declaring.Assembly.GetReferencedAssemblies())
        {
            if (reference.Name?.StartsWith("Orkeon.", StringComparison.Ordinal) != true)
                continue;

            try
            {
                var found = Assembly.Load(reference).GetTypes()
                    .FirstOrDefault(type => string.Equals(Name(type), typeName, StringComparison.Ordinal));
                if (found is not null)
                    return found;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or ReflectionTypeLoadException)
            {
                // An assembly that cannot be read names nothing: the reference keeps its two names.
            }
        }

        return null;
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

    /// <summary>
    /// The space a removed bracket or a nested element leaves before a mark that closes a clause.
    /// A dot that opens a name — ".NET", ".ork.ts" — closes nothing: its space stays.
    /// </summary>
    [GeneratedRegex(@"\s+([,.;:])(?=\s|$|[)\]])")]
    private static partial Regex SpaceBeforePunctuation();

    /// <summary>"Gets or sets ", "Gets ", and the "a value indicating " that may follow, at the head of a summary.</summary>
    [GeneratedRegex(@"^Gets (?:or sets )?(?:a value indicating )?")]
    private static partial Regex Accessor();
}
