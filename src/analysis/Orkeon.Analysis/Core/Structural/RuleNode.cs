using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>One node of a parsed structural rule: the closed subset of ast-grep's rule keys.</summary>
internal abstract record RuleNode;

internal sealed record KindRule(string Kind) : RuleNode;

internal sealed record PatternRule(string Pattern) : RuleNode;

internal sealed record RegexRule(Regex Regex) : RuleNode;

internal sealed record FieldRule(string Field) : RuleNode;

internal sealed record NotRule(RuleNode Rule) : RuleNode;

internal sealed record AllRule(IReadOnlyList<RuleNode> Rules) : RuleNode;

internal sealed record AnyRule(IReadOnlyList<RuleNode> Rules) : RuleNode;

internal enum Relation
{
    Inside,
    Has,
}

/// <summary>Where a relational rule stops walking: one step (neighbor), to the root or the leaves (end), or past a rule.</summary>
internal sealed record StopBy(bool ToEnd, RuleNode? Until);

internal sealed record RelationalRule(Relation Relation, RuleNode Rule, StopBy Stop) : RuleNode;

internal static class RuleNodes
{
    public static IEnumerable<string> PatternsOf(RuleNode node) => node switch
    {
        PatternRule p => [p.Pattern],
        NotRule n => PatternsOf(n.Rule),
        AllRule a => a.Rules.SelectMany(PatternsOf),
        AnyRule a => a.Rules.SelectMany(PatternsOf),
        RelationalRule r => PatternsOf(r.Rule).Concat(r.Stop.Until is null ? [] : PatternsOf(r.Stop.Until)),
        _ => [],
    };
}
