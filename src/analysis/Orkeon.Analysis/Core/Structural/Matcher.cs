using System.Collections.Immutable;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Structural;

internal sealed class Matcher
{
    private readonly TreeSitterParserPool _pool;
    private (StructuralRule Rule, Dictionary<string, CompiledPattern> Patterns)? _compiled;

    public Matcher(TreeSitterParserPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        _pool = pool;
    }

    public IReadOnlyList<StructuralMatch> Match(string virtualPath, ReadOnlyMemory<byte> bytes, StructuralRule rule, Func<StructuralFile>? outlineSource = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var bom = bytes.Span.StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var source = Encoding.UTF8.GetString(bytes.Span[bom..]);
        if (rule.Language == "markdown") return MarkdownMatcher.Match(virtualPath, source, rule);

        var patterns = CompiledFor(rule);
        IReadOnlyList<StructuralNode>? outline = null;

        using var parsed = _pool.Parse(source, rule.Language);
        var evaluator = new RuleEvaluator(rule.Root, patterns, source);
        var matches = new List<StructuralMatch>();
        var stack = new Stack<TsNode>();
        stack.Push(parsed.Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var children = node.Children.ToList();
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);

            var captures = evaluator.Match(node);
            if (captures is null) continue;

            var startByte = bom + Encoding.UTF8.GetByteCount(source.AsSpan(0, node.StartIndex));
            var endByte = startByte + Encoding.UTF8.GetByteCount(source.AsSpan(node.StartIndex, node.EndIndex - node.StartIndex));
            matches.Add(new StructuralMatch
            {
                Path = virtualPath,
                Line = node.StartPosition.Row + 1,
                Column = node.StartPosition.Column + 1,
                StartByte = startByte,
                EndByte = endByte,
                Text = source[node.StartIndex..node.EndIndex],
                EnclosingSelector = (outline ??= outlineSource?.Invoke().Outline() ?? [])
                    .Where(o => o.Range.StartByte <= startByte && endByte <= o.Range.EndByte)
                    .OrderBy(o => o.Range.EndByte - o.Range.StartByte)
                    .FirstOrDefault()?.Selector,
                Captures = captures,
            });
        }

        return matches;
    }

    private Dictionary<string, CompiledPattern> CompiledFor(StructuralRule rule)
    {
        if (_compiled is { } cached && ReferenceEquals(cached.Rule, rule)) return cached.Patterns;
        var patterns = RuleNodes.PatternsOf(rule.Root).Distinct(StringComparer.Ordinal)
            .ToDictionary(p => p, p => PatternCompiler.Compile(p, rule.Language, _pool), StringComparer.Ordinal);
        _compiled = (rule, patterns);
        return patterns;
    }

    internal static ImmutableDictionary<string, string>? MatchNode(
        PatternNode pattern, TsNode node, string source, ImmutableDictionary<string, string> captures)
    {
        if (pattern.Kind == PatternKind.Single)
        {
            return node.IsNamed ? Bind(captures, pattern.Name, source[node.StartIndex..node.EndIndex]) : null;
        }

        if (pattern.Type != node.Type) return null;
        var targets = node.Children.Where(c => c.Type != "comment").ToList();
        if (pattern.Children.Count == 0) return targets.Count == 0 && pattern.Text == node.Text ? captures : null;

        return MatchSequence(pattern.Children, 0, targets, 0, source, captures);
    }

    private static ImmutableDictionary<string, string>? MatchSequence(
        IReadOnlyList<PatternNode> pattern, int p, List<TsNode> targets, int t, string source,
        ImmutableDictionary<string, string> captures)
    {
        if (p == pattern.Count) return t == targets.Count ? captures : null;

        if (pattern[p].Kind == PatternKind.Variadic)
        {
            for (var take = 0; take <= targets.Count - t; take++)
            {
                var text = take == 0 ? string.Empty : source[targets[t].StartIndex..targets[t + take - 1].EndIndex];
                var bound = Bind(captures, pattern[p].Name, text);
                if (bound is null) continue;
                var rest = MatchSequence(pattern, p + 1, targets, t + take, source, bound);
                if (rest is not null) return rest;
            }

            return null;
        }

        if (t == targets.Count) return null;
        var matched = MatchNode(pattern[p], targets[t], source, captures);
        return matched is null ? null : MatchSequence(pattern, p + 1, targets, t + 1, source, matched);
    }

    private static ImmutableDictionary<string, string>? Bind(ImmutableDictionary<string, string> captures, string name, string text)
    {
        if (captures.TryGetValue(name, out var existing)) return existing == text ? captures : null;
        return captures.Add(name, text);
    }
}
