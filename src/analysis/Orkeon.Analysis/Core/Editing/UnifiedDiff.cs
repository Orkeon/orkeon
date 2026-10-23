using System.Text;

namespace Orkeon.Analysis.Core.Editing;

internal static class UnifiedDiff
{
    private const int Context = 3;

    private readonly record struct Line(char Op, string Text);

    public static string Write(string path, string before, string after)
    {
        var script = Script(Split(before), Split(after));
        var changes = new List<int>();
        for (var i = 0; i < script.Count; i++)
            if (script[i].Op != ' ') changes.Add(i);
        if (changes.Count == 0) return "";

        var sb = new StringBuilder().Append("--- a/").Append(path).Append('\n').Append("+++ b/").Append(path).Append('\n');
        var k = 0;
        while (k < changes.Count)
        {
            var first = changes[k];
            var last = first;
            while (++k < changes.Count && changes[k] - last <= 2 * Context + 1) last = changes[k];
            AppendHunk(sb, script, Math.Max(0, first - Context), Math.Min(script.Count - 1, last + Context));
        }

        return sb.ToString();
    }

    private static void AppendHunk(StringBuilder sb, List<Line> script, int from, int to)
    {
        int aStart = 1, bStart = 1;
        for (var i = 0; i < from; i++)
        {
            if (script[i].Op != '+') aStart++;
            if (script[i].Op != '-') bStart++;
        }

        int aCount = 0, bCount = 0;
        for (var i = from; i <= to; i++)
        {
            if (script[i].Op != '+') aCount++;
            if (script[i].Op != '-') bCount++;
        }

        if (aCount == 0) aStart--;
        if (bCount == 0) bStart--;
        sb.Append("@@ -").Append(aStart).Append(',').Append(aCount).Append(" +").Append(bStart).Append(',').Append(bCount).Append(" @@\n");
        for (var i = from; i <= to; i++) sb.Append(script[i].Op).Append(script[i].Text).Append('\n');
    }

    private static string[] Split(string text)
    {
        if (text.Length == 0) return [];
        var lines = text.Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    private static List<Line> Script(string[] a, string[] b)
    {
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;

        var n = a.Length - prefix - suffix;
        var m = b.Length - prefix - suffix;
        var lcs = new int[n + 1][];
        for (var i = 0; i <= n; i++) lcs[i] = new int[m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            lcs[i][j] = a[prefix + i] == b[prefix + j] ? lcs[i + 1][j + 1] + 1 : Math.Max(lcs[i + 1][j], lcs[i][j + 1]);

        var script = new List<Line>(a.Length + m);
        for (var i = 0; i < prefix; i++) script.Add(new Line(' ', a[i]));
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[prefix + x] == b[prefix + y])
            {
                script.Add(new Line(' ', a[prefix + x]));
                x++;
                y++;
            }
            else if (x < n && (y == m || lcs[x + 1][y] >= lcs[x][y + 1]))
            {
                script.Add(new Line('-', a[prefix + x++]));
            }
            else
            {
                script.Add(new Line('+', b[prefix + y++]));
            }
        }

        for (var i = a.Length - suffix; i < a.Length; i++) script.Add(new Line(' ', a[i]));
        return script;
    }
}
