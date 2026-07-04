namespace Orkeon.Cli.Abstractions.Console;

/// <summary>Result of completing the token under the cursor.</summary>
/// <param name="NewLeft">Replacement for the text left of the cursor (head + sigil + completed core).</param>
/// <param name="Candidates">Candidates considered (always ≥ 1). Count &gt; 1 ⇒ ambiguous, and
/// <paramref name="NewLeft"/> carries the longest common prefix.</param>
public readonly record struct ReplCompletionOutcome(string NewLeft, IReadOnlyList<string> Candidates);

/// <summary>
/// Shared REPL Tab-completion logic over an <see cref="IReplInputAssist"/>, used by both the
/// Terminal.Gui input pane and the plain-mode line editor so the two surfaces behave identically.
/// </summary>
public static class ReplCompletion
{
    /// <summary>
    /// Completes the token at the end of <paramref name="left"/> (the text left of the cursor):
    /// a leading <c>/</c> on the first token completes command names; an <c>@</c> token completes
    /// virtual paths. Returns <see langword="null"/> when nothing applies or there is no candidate.
    /// </summary>
    public static ReplCompletionOutcome? CompleteLeft(string left, IReplInputAssist assist)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(assist);

        var tokenStart = left.LastIndexOf(' ') + 1; // 0 when there is no preceding space
        var token = left[tokenStart..];
        var head = left[..tokenStart];

        if (tokenStart == 0 && token.StartsWith('/'))
            return Build(head, "/", assist.CompleteCommand(token[1..]));
        if (token.StartsWith('@'))
            return Build(head, "@", assist.CompletePath(token[1..]));
        return null;
    }

    private static ReplCompletionOutcome? Build(string head, string sigil, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0) return null;
        var core = candidates.Count == 1 ? candidates[0] : LongestCommonPrefix(candidates);
        return new ReplCompletionOutcome(head + sigil + core, candidates);
    }

    /// <summary>Case-insensitive longest common prefix, preserving the first candidate's casing.</summary>
    public static string LongestCommonPrefix(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return string.Empty;

        var prefix = items[0];
        for (var i = 1; i < items.Count && prefix.Length > 0; i++)
        {
            var s = items[i];
            var n = Math.Min(prefix.Length, s.Length);
            var k = 0;
            while (k < n && char.ToLowerInvariant(prefix[k]) == char.ToLowerInvariant(s[k])) k++;
            prefix = prefix[..k];
        }
        return prefix;
    }
}
