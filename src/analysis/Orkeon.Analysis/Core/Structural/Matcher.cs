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

            if (!evaluator.TryMatch(node, out var captures)) continue;

            var startByte = bom + Encoding.UTF8.GetByteCount(source.AsSpan(0, node.StartIndex));
            var endByte = startByte + Encoding.UTF8.GetByteCount(source.AsSpan(node.StartIndex, node.EndIndex - node.StartIndex));
            outline ??= outlineSource?.Invoke().Outline() ?? [];
            matches.Add(new StructuralMatch
            {
                Path = virtualPath,
                Line = node.StartPosition.Row + 1,
                Column = node.StartPosition.Column + 1,
                StartByte = startByte,
                EndByte = endByte,
                Text = source[node.StartIndex..node.EndIndex],
                EnclosingSelector = outline
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

    internal static bool TryMatchNode(
        PatternNode pattern, TsNode node, string source, ImmutableDictionary<string, string> captures,
        out ImmutableDictionary<string, string> result)
    {
        result = captures;
        if (pattern.Kind == PatternKind.Single)
        {
            return node.IsNamed && TryBind(captures, pattern.Name, source[node.StartIndex..node.EndIndex], out result);
        }

        if (pattern.Type != node.Type) return false;
        var targets = node.Children.Where(c => !StructuralFile.IsComment(c.Type)).ToList();
        if (pattern.Children.Count == 0) return targets.Count == 0 && pattern.Text == node.Text;

        return TryMatchSequence(pattern.Children, 0, targets, 0, source, captures, out result);
    }

    private static bool TryMatchSequence(
        IReadOnlyList<PatternNode> pattern, int p, List<TsNode> targets, int t, string source,
        ImmutableDictionary<string, string> captures, out ImmutableDictionary<string, string> result)
    {
        result = captures;
        if (p == pattern.Count) return t == targets.Count;
        if (pattern[p].Kind == PatternKind.Variadic) return TryMatchVariadic(pattern, p, targets, t, source, captures, out result);
        if (t == targets.Count) return false;

        return TryMatchNode(pattern[p], targets[t], source, captures, out var matched)
            && TryMatchSequence(pattern, p + 1, targets, t + 1, source, matched, out result);
    }

    // A variadic metavariable takes the fewest targets that let the rest of the sequence match.
    private static bool TryMatchVariadic(
        IReadOnlyList<PatternNode> pattern, int p, List<TsNode> targets, int t, string source,
        ImmutableDictionary<string, string> captures, out ImmutableDictionary<string, string> result)
    {
        for (var take = 0; take <= targets.Count - t; take++)
        {
            var text = take == 0 ? string.Empty : source[targets[t].StartIndex..targets[t + take - 1].EndIndex];
            if (TryBind(captures, pattern[p].Name, text, out var bound)
                && TryMatchSequence(pattern, p + 1, targets, t + take, source, bound, out result))
            {
                return true;
            }
        }

        result = captures;
        return false;
    }

    // A metavariable bound twice must capture the same text both times.
    private static bool TryBind(
        ImmutableDictionary<string, string> captures, string name, string text, out ImmutableDictionary<string, string> result)
    {
        result = captures;
        if (captures.TryGetValue(name, out var existing)) return existing == text;
        result = captures.Add(name, text);
        return true;
    }
}
