namespace Orkeon.Analysis.Core.Editing;

/// <summary>
/// A readable selector (<c>Ns.Type.Member(paramTypes)</c>) or a tree-sitter path
/// (<c>@ts:root/&lt;i&gt;/&lt;kind&gt;/...</c>, <c>i</c> the 0-based index among the named children).
/// </summary>
internal sealed record NodeSelector(string Text)
{
    private const string PathPrefix = "@ts:";

    public bool IsTreeSitterPath => Text.StartsWith(PathPrefix, StringComparison.Ordinal);

    /// <summary>Readable form: the dotted name without its parameter list.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Readable form: the parameter types when the selector spells a parameter list.</summary>
    public IReadOnlyList<string>? ParameterTypes { get; init; }

    /// <summary>Path form: root kind, then (index, kind) pairs.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public static NodeSelector Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.StartsWith(PathPrefix, StringComparison.Ordinal))
        {
            var steps = text[PathPrefix.Length..].Split('/');
            if (steps.Length % 2 == 0 || steps.Any(s => s.Length == 0))
                throw new FormatException($"Malformed tree-sitter path '{text}'.");
            return new NodeSelector(text) { Steps = steps };
        }

        var open = text.IndexOf('(', StringComparison.Ordinal);
        if (open < 0) return new NodeSelector(text) { Name = text };
        if (!text.EndsWith(')')) throw new FormatException($"Malformed selector '{text}'.");
        return new NodeSelector(text)
        {
            Name = text[..open],
            ParameterTypes = SplitTypes(text[(open + 1)..^1]),
        };
    }

    internal static string Compact(string type) => string.Concat(type.Where(c => !char.IsWhiteSpace(c)));

    private static List<string> SplitTypes(string list)
    {
        var types = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            switch (list[i])
            {
                case '<' or '[' or '(': depth++; break;
                case '>' or ']' or ')': depth--; break;
                case ',' when depth == 0:
                    types.Add(Compact(list[start..i]));
                    start = i + 1;
                    break;
            }
        }
        var last = Compact(list[start..]);
        if (last.Length > 0 || types.Count > 0) types.Add(last);
        return types;
    }
}
