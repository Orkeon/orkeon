using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core;

internal sealed class GitignoreMatcher
{
    private readonly List<GitignorePattern> _patterns;

    public GitignoreMatcher(IEnumerable<string> gitignoreLines)
    {
        _patterns = [];
        foreach (var raw in gitignoreLines)
        {
            var line = TrimUnescapedTrailingSpaces(raw);
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var negate = line.StartsWith('!');
            if (negate) line = line[1..];

            var dirOnly = line.EndsWith('/');
            if (dirOnly) line = line[..^1];

            var anchored = line.Contains('/', StringComparison.Ordinal);
            if (line.StartsWith('/')) line = line[1..];

            _patterns.Add(new GitignorePattern(GlobToRegex(line, anchored), negate, dirOnly));
        }
    }

    public bool IsIgnored(string relativePath, bool isDirectory) => Match(relativePath, isDirectory) == true;

    /// <summary>
    /// The verdict of the last pattern matching <paramref name="relativePath"/>: <see langword="true"/> ignored,
    /// <see langword="false"/> re-included by a negation, <see langword="null"/> when no pattern matches, so a
    /// caller layering several files can let the next one decide.
    /// </summary>
    public bool? Match(string relativePath, bool isDirectory)
    {
        var path = relativePath.Replace('\\', '/');
        bool? ignored = null;
        foreach (var p in _patterns)
        {
            if (p.DirectoryOnly && !isDirectory) continue;
            if (p.Regex.IsMatch(path)) ignored = !p.Negate;
        }
        return ignored;
    }

    // Only spaces not escaped by a backslash are stripped; an escaped one is part of the name.
    private static string TrimUnescapedTrailingSpaces(string line)
    {
        var end = line.Length;
        while (end > 0 && line[end - 1] == ' ')
        {
            var backslashes = 0;
            while (end - 2 - backslashes >= 0 && line[end - 2 - backslashes] == '\\') backslashes++;
            if (backslashes % 2 == 1) break;
            end--;
        }
        return line[..end];
    }

    private static Regex GlobToRegex(string pattern, bool anchored)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(anchored ? "^" : "(^|/)");
        for (int i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    i = AppendStar(sb, pattern, i);
                    break;
                case '?': sb.Append("[^/]"); break;
                case '[':
                    i = AppendCharacterClass(sb, pattern, i);
                    break;
                case '\\' when i + 1 < pattern.Length:
                    sb.Append(Regex.Escape(pattern[++i].ToString()));
                    break;
                case '.': case '+': case '(': case ')': case '|': case '^': case '$':
                case '{': case '}': case ']': case '\\':
                    sb.Append('\\').Append(c); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("(/|$)");
        return new Regex(sb.ToString(), RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    }

    // Translates the '*' or '**' at pattern[index] and returns the index of the last character consumed.
    private static int AppendStar(System.Text.StringBuilder sb, string pattern, int index)
    {
        if (index + 1 >= pattern.Length || pattern[index + 1] != '*')
        {
            sb.Append("[^/]*");
            return index;
        }

        var atSegmentStart = index == 0 || pattern[index - 1] == '/';
        if (atSegmentStart && index + 2 < pattern.Length && pattern[index + 2] == '/')
        {
            sb.Append("(.*/)?");
            return index + 2;
        }

        sb.Append(".*");
        return index + 1;
    }

    // Copies the class opening at pattern[start] as a regex class and returns the index of its
    // closing ']'. A '[' with no closing ']' stays a literal. As in git, a leading '!' or '^'
    // negates, a ']' right after the opening (or the negation) is a member, and no class matches '/'.
    private static int AppendCharacterClass(System.Text.StringBuilder sb, string pattern, int start)
    {
        var i = start + 1;
        var negate = i < pattern.Length && pattern[i] is '!' or '^';
        if (negate) i++;
        var first = i;
        while (i < pattern.Length && (pattern[i] != ']' || i == first))
        {
            if (pattern[i] == '\\') i++;
            i++;
        }
        if (i >= pattern.Length)
        {
            sb.Append("\\[");
            return start;
        }

        sb.Append('[');
        if (negate) sb.Append('^');
        AppendClassMembers(sb, pattern, first, i);
        if (negate) sb.Append('/');
        sb.Append(']');
        return i;
    }

    private static void AppendClassMembers(System.Text.StringBuilder sb, string pattern, int first, int end)
    {
        var i = end;
        for (var j = first; j < i; j++)
        {
            var c = pattern[j];
            if (c == '\\' && j + 1 < i) c = pattern[++j];
            else if (c == '-' && j != first && j != i - 1)
            {
                sb.Append('-');
                continue;
            }
            if (c is '\\' or ']' or '[' or '^' or '-') sb.Append('\\');
            sb.Append(c);
        }
    }

    private sealed record GitignorePattern(Regex Regex, bool Negate, bool DirectoryOnly);
}
