using System.Collections.Immutable;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>Evaluates a parsed rule on the live nodes of one parsed file; patterns are compiled by the caller, once per call.</summary>
internal sealed class RuleEvaluator(RuleNode root, IReadOnlyDictionary<string, CompiledPattern> patterns, string source)
{
    private static readonly ImmutableDictionary<string, string> Empty = ImmutableDictionary<string, string>.Empty;

    /// <summary>True when the rule matches <paramref name="node"/>, with its captures in <paramref name="captures"/>.</summary>
    public bool TryMatch(TsNode node, out ImmutableDictionary<string, string> captures) => TryEval(root, node, out captures);

    private bool TryEval(RuleNode rule, TsNode node, out ImmutableDictionary<string, string> captures)
    {
        captures = Empty;
        switch (rule)
        {
            case KindRule k: return node.Type == k.Kind;
            case PatternRule p: return Matcher.TryMatchNode(patterns[p.Pattern].Root, node, source, Empty, out captures);
            case RegexRule r: return r.Regex.IsMatch(source.AsSpan(node.StartIndex, node.EndIndex - node.StartIndex));
            case FieldRule f: return FieldOf(node) == f.Field;
            case NotRule n: return !Matches(n.Rule, node);
            case AnyRule any: return TryAny(any, node, out captures);
            case AllRule all: return TryAll(all, node, out captures);
            case RelationalRule rel when rel.Relation == Relation.Has: return Has(rel, node);
            case RelationalRule rel: return Inside(rel, node);
            default: throw new InvalidOperationException($"Unknown rule node {rule.GetType().Name}.");
        }
    }

    private bool Matches(RuleNode rule, TsNode node) => TryEval(rule, node, out _);

    // The captures of the first rule that matches.
    private bool TryAny(AnyRule any, TsNode node, out ImmutableDictionary<string, string> captures)
    {
        foreach (var rule in any.Rules)
        {
            if (TryEval(rule, node, out captures)) return true;
        }

        captures = Empty;
        return false;
    }

    private bool TryAll(AllRule all, TsNode node, out ImmutableDictionary<string, string> captures)
    {
        captures = Empty;
        foreach (var rule in all.Rules)
        {
            if (!TryEval(rule, node, out var matched)) return false;
            captures = captures.SetItems(matched);
        }

        return true;
    }

    private bool Has(RelationalRule rule, TsNode node)
    {
        foreach (var child in node.Children)
        {
            if (Matches(rule.Rule, child)) return true;
            if (Continues(rule.Stop, child) && Has(rule, child)) return true;
        }

        return false;
    }

    private bool Inside(RelationalRule rule, TsNode node)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (Matches(rule.Rule, ancestor)) return true;
            if (!Continues(rule.Stop, ancestor)) return false;
        }

        return false;
    }

    private bool Continues(StopBy stop, TsNode node) =>
        stop.ToEnd || (stop.Until is not null && !Matches(stop.Until, node));

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
