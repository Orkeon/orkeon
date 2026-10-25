using System.Text;
using System.Text.RegularExpressions;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

/// <summary>Text helpers shared by the adapters: signatures cut before the body, virtual path joins.</summary>
internal static class AdapterText
{
    private const int MaxSignatureLength = 500;

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    /// <summary>
    /// The declaration text up to its body - the <c>body</c> field, else the first child whose type is in
    /// <paramref name="bodyTypes"/> - whitespace collapsed, trailing <c>;</c> dropped, capped at 500 characters.
    /// </summary>
    public static string Signature(TsNode node, IReadOnlySet<string> bodyTypes)
    {
        ArgumentNullException.ThrowIfNull(node);
        var body = node.GetChildForField("body") ?? node.Children.FirstOrDefault(c => bodyTypes.Contains(c.Type));
        return SignatureUpTo(node, body is not null && body.StartIndex > node.StartIndex ? body.StartIndex : node.EndIndex);
    }

    /// <summary>The declaration text up to the byte offset <paramref name="endIndex"/>, normalised as <see cref="Signature"/>.</summary>
    public static string SignatureUpTo(TsNode node, int endIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        var bytes = Encoding.UTF8.GetBytes(node.Text ?? string.Empty);
        var length = Math.Clamp(endIndex - node.StartIndex, 0, bytes.Length);
        var signature = WhitespaceRegex.Replace(Encoding.UTF8.GetString(bytes, 0, length), " ").Trim().TrimEnd(';').TrimEnd();
        return signature.Length > MaxSignatureLength ? signature[..MaxSignatureLength] + "…" : signature;
    }

    /// <summary>A path relative to the directory of <paramref name="currentVirtualFilePath"/>, <c>.</c> and <c>..</c> folded.</summary>
    public static string JoinRelative(string currentVirtualFilePath, string relativePath)
    {
        var current = currentVirtualFilePath.Replace('\\', '/');
        var lastSlash = current.LastIndexOf('/');
        var dir = lastSlash > 0 ? current[..lastSlash] : string.Empty;
        var specifier = relativePath.Replace('\\', '/');
        var combined = specifier.StartsWith('/') ? specifier : dir + "/" + specifier;

        var stack = new List<string>();
        foreach (var part in combined.Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }

        var joined = string.Join('/', stack);
        return combined.StartsWith('/') ? "/" + joined : joined;
    }

    /// <summary>The text of a string literal without its surrounding quotes.</summary>
    public static string Unquote(string literal) => literal.Trim().Trim('\'', '"', '`');

    /// <summary>The first ancestor of <paramref name="node"/> whose type is in <paramref name="types"/>, if any.</summary>
    public static TsNode? Ancestor(TsNode node, params ReadOnlySpan<string> types)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (types.Contains(parent.Type)) return parent;
        }
        return null;
    }
}
