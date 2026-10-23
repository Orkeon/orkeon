using Orkeon.Analysis.TreeSitter;
using YamlDotNet.Serialization;

namespace Orkeon.Analysis.Core.Structural;

internal sealed class StructuralRule
{
    private StructuralRule(string language, RuleNode root, string id = "", string? message = null, string? severity = null, string? fix = null)
    {
        Language = LanguageRegistry.Resolve(language);
        Root = root;
        Id = id;
        Message = message;
        Severity = severity;
        Fix = fix;
    }

    public string Language { get; }

    public RuleNode Root { get; }

    public string Id { get; }

    public string? Message { get; }

    public string? Severity { get; }

    public string? Fix { get; }

    public IReadOnlySet<string> MetavariableNames =>
        RuleNodes.PatternsOf(Root).SelectMany(MetavariableGrammar.NamesIn).ToHashSet(StringComparer.Ordinal);

    public static StructuralRule FromInline(string language, IReadOnlyDictionary<string, object?> rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(rule);
        return new StructuralRule(language, RuleParser.Parse(rule));
    }

    public static StructuralRule FromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var doc = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(yaml)
            ?? throw new ArgumentException("The rule file is empty.", nameof(yaml));
        var language = Text(doc, "language") ?? throw new ArgumentException("The rule file needs a 'language'.", nameof(yaml));
        if (!doc.TryGetValue("rule", out var rule)) throw new ArgumentException("The rule file needs a 'rule'.", nameof(yaml));
        return new StructuralRule(language, RuleParser.Parse(rule), Text(doc, "id") ?? string.Empty, Text(doc, "message"), Text(doc, "severity"), Text(doc, "fix"));
    }

    public static StructuralRule FromPattern(string language, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return new StructuralRule(language, new PatternRule(pattern));
    }

    private static string? Text(Dictionary<string, object?> doc, string key) =>
        doc.TryGetValue(key, out var value) ? value as string : null;
}
