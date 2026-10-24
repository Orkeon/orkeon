using System.Collections.Immutable;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>Evaluates a parsed rule on the live nodes of one parsed file; patterns are compiled by the caller, once per call.</summary>
internal sealed class RuleEvaluator(RuleNode root, IReadOnlyDictionary<string, CompiledPattern> patterns, string source)
{
    private static readonly ImmutableDictionary<string, string> Empty = ImmutableDictionary<string, string>.Empty;

    /// <summary>The captures of the rule on <paramref name="node"/>, or null when it does not match.</summary>
    public ImmutableDictionary<string, string>? Match(TsNode node) => Eval(root, node);

    private ImmutableDictionary<string, string>? Eval(RuleNode rule, TsNode node)
    {
        switch (rule)
        {
            case KindRule k: return node.Type == k.Kind ? Empty : null;
            case PatternRule p: return Matcher.MatchNode(patterns[p.Pattern].Root, node, source, Empty);
            case RegexRule r: return r.Regex.IsMatch(source.AsSpan(node.StartIndex, node.EndIndex - node.StartIndex)) ? Empty : null;
            case FieldRule f: return FieldOf(node) == f.Field ? Empty : null;
            case NotRule n: return Eval(n.Rule, node) is null ? Empty : null;
            case AnyRule any: return any.Rules.Select(r => Eval(r, node)).FirstOrDefault(c => c is not null);
            case AllRule all: return All(all, node);
            case RelationalRule rel: return (rel.Relation == Relation.Has ? Has(rel, node) : Inside(rel, node)) ? Empty : null;
            default: throw new InvalidOperationException($"Unknown rule node {rule.GetType().Name}.");
        }
    }

    private ImmutableDictionary<string, string>? All(AllRule all, TsNode node)
    {
        var captures = Empty;
        foreach (var rule in all.Rules)
        {
            var matched = Eval(rule, node);
            if (matched is null) return null;
            captures = captures.SetItems(matched);
        }

        return captures;
    }

    private bool Has(RelationalRule rule, TsNode node)
    {
        foreach (var child in node.Children)
        {
            if (Eval(rule.Rule, child) is not null) return true;
            if (Continues(rule.Stop, child) && Has(rule, child)) return true;
        }

        return false;
    }

    private bool Inside(RelationalRule rule, TsNode node)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (Eval(rule.Rule, ancestor) is not null) return true;
            if (!Continues(rule.Stop, ancestor)) return false;
        }

        return false;
    }

    private bool Continues(StopBy stop, TsNode node) =>
        stop.ToEnd || (stop.Until is not null && Eval(stop.Until, node) is null);

    private static string? FieldOf(TsNode node)
    {
        if (node.Parent is not { } parent) return null;
        var children = parent.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].Equals(node)) return parent.GetFieldNameForChild(i);
        }

        return null;
    }
}
