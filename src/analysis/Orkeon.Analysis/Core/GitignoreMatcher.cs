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

    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        var path = relativePath.Replace('\\', '/');
        var ignored = false;
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
                case '.': case '+': case '(': case ')': case '|': case '^': case '$':
                case '{': case '}': case '[': case ']': case '\\':
                    sb.Append('\\').Append(c); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("(/|$)");
        return new Regex(sb.ToString(), RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    }

    private sealed record GitignorePattern(Regex Regex, bool Negate, bool DirectoryOnly);
}
