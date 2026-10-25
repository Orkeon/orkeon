using System.Text.RegularExpressions;
using Orkeon.Analysis.Core.Editing;
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
    // Statement nodes a lone expression pattern parses inside; the root of the parse is unwrapped in any grammar.
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "global_statement", "expression_statement",
    };

    // A JSON pattern quotes its sentinels, as an identifier is no JSON value.
    [GeneratedRegex(@"^""?__ork_(mv|va)_(.+?)__""?$")]
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
        var source = language == "json"
            ? MetavariableGrammar.Token().Replace(pattern, token => token.Groups[1].Success || token.Groups[2].Success ? $"\"{Placeholder(token)}\"" : Placeholder(token))
            : MetavariableGrammar.Token().Replace(pattern, Placeholder);
        // PHP code parses only after its opening tag; the tag is skipped when the pattern is unwrapped.
        if (language == "php" && !source.TrimStart().StartsWith("<?", StringComparison.Ordinal)) source = "<?php " + source;
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
        for (var atRoot = true; atRoot || Wrappers.Contains(node.Type); atRoot = false)
        {
            var named = node.Children.Where(c => c.IsNamed && !StructuralFile.IsComment(c.Type) && c.Type != "php_tag").ToList();
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

        var children = node.Children.Where(c => !StructuralFile.IsComment(c.Type)).Select(Build).ToList();
        return new PatternNode(node.Type, text, PatternKind.Literal, string.Empty, children);
    }
}
