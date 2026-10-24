using System.Text.RegularExpressions;
using Orkeon.Analysis.TreeSitter;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Structural;

internal enum PatternKind
{
    Literal,
    Single,
    Variadic,
}

/// <summary>Immutable snapshot of one node of the parsed pattern; no live tree is kept.</summary>
internal sealed record PatternNode(string Type, string Text, PatternKind Kind, string Name, IReadOnlyList<PatternNode> Children);

internal sealed record CompiledPattern(PatternNode Root);

/// <summary>
/// Parses a pattern with the target grammar after replacing each <c>$NAME</c> / <c>$$$NAME</c> by a sentinel
/// identifier, then snapshots the tree with the sentinels recognised as metavariables.
/// </summary>
internal static partial class PatternCompiler
{
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "compilation_unit", "program", "global_statement", "expression_statement",
    };

    [GeneratedRegex(@"^__ork_(mv|va)_(.+)__$")]
    private static partial Regex Sentinel();

    // A variadic or single metavariable becomes a parsable sentinel identifier; an escaped dollar stays a dollar.
    private static string Placeholder(Match token)
    {
        if (token.Groups[1].Success) return $"__ork_va_{token.Groups[1].Value}__";
        if (token.Groups[2].Success) return $"__ork_mv_{token.Groups[2].Value}__";
        return MetavariableGrammar.LiteralDollar;
    }

    public static CompiledPattern Compile(string pattern, string language, TreeSitterParserPool pool)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(pool);
        var source = MetavariableGrammar.Token().Replace(pattern, Placeholder);
        using var parsed = pool.Parse(source, language);
        if (!parsed.Root.HasError) return Snapshot(parsed.Root);

        using var retried = pool.Parse(source + ";", language);
        if (retried.Root.HasError)
            throw new ArgumentException($"The pattern '{pattern}' does not parse as {language}.", nameof(pattern));
        return Snapshot(retried.Root);
    }

    private static CompiledPattern Snapshot(TsNode root)
    {
        var node = root;
        while (Wrappers.Contains(node.Type))
        {
            var named = node.Children.Where(c => c.IsNamed && c.Type != "comment").ToList();
            if (named.Count != 1) break;
            node = named[0];
        }

        return new CompiledPattern(Build(node));
    }

    private static PatternNode Build(TsNode node)
    {
        var text = node.Text;
        var sentinel = Sentinel().Match(text);
        if (sentinel.Success)
        {
            var kind = sentinel.Groups[1].Value == "va" ? PatternKind.Variadic : PatternKind.Single;
            return new PatternNode(node.Type, text, kind, sentinel.Groups[2].Value, []);
        }

        var children = node.Children.Where(c => c.Type != "comment").Select(Build).ToList();
        return new PatternNode(node.Type, text, PatternKind.Literal, string.Empty, children);
    }
}
