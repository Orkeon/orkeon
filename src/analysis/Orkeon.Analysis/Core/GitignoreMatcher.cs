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
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var negate = line.StartsWith('!');
            if (negate) line = line[1..];

            var dirOnly = line.EndsWith('/');
            if (dirOnly) line = line[..^1];

            var anchored = line.StartsWith('/');
            if (anchored) line = line[1..];

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
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
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
        if (negate) sb.Append('/');
        sb.Append(']');
        return i;
    }

    private sealed record GitignorePattern(Regex Regex, bool Negate, bool DirectoryOnly);
}
