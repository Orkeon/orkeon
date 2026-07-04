using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Fingerprinters;

public sealed class FrameworkFingerprinter : IFrameworkFingerprinter
{
    private static readonly Regex HttpVerbRegex = new(
        @"^(?:@|\[)?(?:app\.|router\.|blueprint\.|Http|http)?(Get|Post|Put|Delete|Patch|Options|Head|get|post|put|delete|patch|options|head)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    private readonly string _framework;
    private readonly IReadOnlyList<FingerprintRule> _rules;

    public FrameworkFingerprinter(string framework, IReadOnlyList<FingerprintRule> rules)
    {
        ArgumentException.ThrowIfNullOrEmpty(framework);
        ArgumentNullException.ThrowIfNull(rules);
        _framework = framework;
        _rules = rules;
    }

    public string Name => _framework;

    public void Apply(IReadOnlyList<RaggableNode> nodes, IReadOnlyList<RaggableEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in nodes)
        {
            if (node.Decorators.Count == 0) continue;
            foreach (var decorator in node.Decorators)
            {
                ApplyDecorator(node, decorator);
            }
        }
    }

    private void ApplyDecorator(RaggableNode node, string decorator)
    {
        var rule = FindMatch(decorator, node.Language);
        if (rule is null) return;

        foreach (var tag in rule.TagsOrEmpty)
        {
            if (!node.Tags.ContainsKey(tag))
                node.TagsMutable[tag] = _framework;
        }

        ApplyHttpVerbTag(decorator, rule, node);

        if (rule.OverrideKind is not null)
            node.OverriddenKind = rule.OverrideKind;
    }

    private FingerprintRule? FindMatch(string decoratorText, string language)
    {
        FingerprintRule? languageMatch = null;
        FingerprintRule? genericMatch = null;
        foreach (var rule in _rules)
        {
            if (!Matches(decoratorText, rule.DecoratorName)) continue;
            if (rule.Language is null && genericMatch is null) genericMatch = rule;
            else if (rule.Language is not null
                && string.Equals(rule.Language, language, StringComparison.OrdinalIgnoreCase)
                && languageMatch is null) languageMatch = rule;
        }
        return languageMatch ?? genericMatch;
    }

    private static bool Matches(string decoratorText, string ruleName)
    {
        var trimmed = decoratorText.TrimStart('@', '[').TrimEnd(']');
        if (trimmed.StartsWith(ruleName, StringComparison.Ordinal))
        {
            var next = trimmed.Length > ruleName.Length ? trimmed[ruleName.Length] : '\0';
            if (next is '\0' or '(' or ' ' or '.' or ']' or '\n' or '\r') return true;
        }
        return false;
    }

    private static void ApplyHttpVerbTag(string decorator, FingerprintRule rule, RaggableNode node)
    {
        if (!rule.TagsOrEmpty.Any(t => t == "http-endpoint")) return;
        var m = HttpVerbRegex.Match(decorator);
        if (!m.Success) return;
#pragma warning disable CA1308 // lowercase verb becomes part of a stored tag key (http-<verb>), not a comparison normalization
        var verb = m.Groups[1].Value.ToLowerInvariant();
#pragma warning restore CA1308
        var tag = $"http-{verb}";
        if (!node.Tags.ContainsKey(tag))
            node.TagsMutable[tag] = rule.Language ?? "";
    }
}
