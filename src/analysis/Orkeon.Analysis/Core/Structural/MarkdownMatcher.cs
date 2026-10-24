using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>Evaluates a structural rule on the F5 Markdown nodes (sections, fences, links, front matter) of one file.</summary>
internal static class MarkdownMatcher
{
    private static readonly MarkdownDocumentMapper Mapper = new();

    public static IReadOnlyList<StructuralMatch> Match(string virtualPath, string markdown, StructuralRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (RuleNodes.PatternsOf(rule.Root).Any()) throw new UnsupportedRuleKeyException("pattern");

        var nodes = Mapper.Map(virtualPath, markdown);
        var evaluator = new Evaluator(nodes);
        return nodes
            .Where(n => evaluator.Matches(rule.Root, n))
            .OrderBy(n => n.Range.StartByte)
            .Select(n => new StructuralMatch
            {
                Path = virtualPath,
                Line = n.Range.StartLine,
                Column = n.Range.StartColumn,
                StartByte = n.Range.StartByte,
                EndByte = n.Range.EndByte,
                Text = n.SourceSnippet,
                Captures = ImmutableDictionary<string, string>.Empty,
            })
            .ToList();
    }

    private sealed class Evaluator
    {
        private readonly IReadOnlyList<RaggableNode> _nodes;
        private readonly Dictionary<string, RaggableNode> _parents = [];

        public Evaluator(IReadOnlyList<RaggableNode> nodes)
        {
            _nodes = nodes;
            foreach (var node in nodes)
            {
                var parent = nodes
                    .Where(s => s.Kind == UniversalNodeKind.Section && s.Id != node.Id
                        && s.Range.StartByte <= node.Range.StartByte && node.Range.EndByte <= s.Range.EndByte
                        && (node.Kind != UniversalNodeKind.Section || s.Range.StartByte < node.Range.StartByte))
                    .MaxBy(s => s.Range.StartByte);
                if (parent is not null) _parents[node.Id] = parent;
            }
        }

        public bool Matches(RuleNode rule, RaggableNode node) => rule switch
        {
            KindRule k => KindOf(node) == k.Kind,
            RegexRule r => r.Regex.IsMatch(TextOf(node)),
            NotRule n => !Matches(n.Rule, node),
            AnyRule any => any.Rules.Any(r => Matches(r, node)),
            AllRule all => all.Rules.All(r => Matches(r, node)),
            RelationalRule { Relation: Relation.Has } rel => Has(rel, node),
            RelationalRule rel => Inside(rel, node),
            FieldRule => throw new UnsupportedRuleKeyException("field"),
            _ => throw new InvalidOperationException($"Unknown rule node {rule.GetType().Name}."),
        };

        private bool Has(RelationalRule rule, RaggableNode node)
        {
            foreach (var child in _nodes.Where(c => _parents.TryGetValue(c.Id, out var p) && p.Id == node.Id))
            {
                if (Matches(rule.Rule, child)) return true;
                if (Continues(rule.Stop, child) && Has(rule, child)) return true;
            }

            return false;
        }

        private bool Inside(RelationalRule rule, RaggableNode node)
        {
            for (var ancestor = ParentOf(node); ancestor is not null; ancestor = ParentOf(ancestor))
            {
                if (Matches(rule.Rule, ancestor)) return true;
                if (!Continues(rule.Stop, ancestor)) return false;
            }

            return false;
        }

        private bool Continues(StopBy stop, RaggableNode node) =>
            stop.ToEnd || (stop.Until is not null && !Matches(stop.Until, node));

        private RaggableNode? ParentOf(RaggableNode node) => _parents.GetValueOrDefault(node.Id);

        private static string KindOf(RaggableNode node) => node.Kind switch
        {
            UniversalNodeKind.Section => "heading",
            UniversalNodeKind.CodeFence => "fence",
            UniversalNodeKind.Link => "link",
            UniversalNodeKind.FrontMatter => "front_matter",
            _ => string.Empty,
        };

        private static string TextOf(RaggableNode node) => node.Kind switch
        {
            UniversalNodeKind.Section => node.Name,
            UniversalNodeKind.CodeFence => node.Tags.Keys.FirstOrDefault() ?? string.Empty,
            _ => node.SourceSnippet,
        };
    }
}
