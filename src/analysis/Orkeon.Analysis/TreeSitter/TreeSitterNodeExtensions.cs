using System.Text.RegularExpressions;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.TreeSitter;

public static class TreeSitterNodeExtensions
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    public static string CleanText(this TsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var text = node.Text ?? string.Empty;
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    public static TsNode? ChildByField(this TsNode node, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (string.IsNullOrEmpty(fieldName)) return null;
        return node.GetChildForField(fieldName);
    }

    public static IEnumerable<TsNode> DescendantsOfType(this TsNode node, string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        var stack = new Stack<TsNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Type == type) yield return current;
            var children = current.Children;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    public static bool IsDocComment(this TsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Type != "comment") return false;
        var text = node.Text ?? string.Empty;
        return text.StartsWith("/**", StringComparison.Ordinal)
            || text.StartsWith("///", StringComparison.Ordinal)
            || text.StartsWith("\"\"\"", StringComparison.Ordinal)
            || text.StartsWith("'''", StringComparison.Ordinal);
    }

    public static string FirstLine(this TsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var text = node.Text ?? string.Empty;
        var idx = text.IndexOf('\n', StringComparison.Ordinal);
        return (idx >= 0 ? text[..idx] : text).TrimEnd('\r');
    }
}
