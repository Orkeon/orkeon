using System.Text;
using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Editing.Intent;

internal sealed partial class EnsureUsingOperation(string name, string? module = null) : IEditOperation
{
    [GeneratedRegex(@"^\s*using\s+(?!static\b)([\w.]+)\s*;\s*$")]
    private static partial Regex PlainUsing();

    [GeneratedRegex(@"^import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*(['""])(.+?)\2\s*;?\s*$")]
    private static partial Regex NamedImport();

    [GeneratedRegex(@"^import\s+(?:type\s+)?\{[^}]*$")]
    private static partial Regex OpenNamedImport();

    [GeneratedRegex(@"^import\b.*['""]\s*;?\s*$")]
    private static partial Regex SingleLineImport();

    [GeneratedRegex(@"from\s*['""](.+?)['""]|^import\s+['""](.+?)['""]")]
    private static partial Regex ModuleOf();

    public EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed)
    {
        if (parsed.Language is not ("csharp" or "typescript"))
            throw new ArgumentException($"ensure_using and ensure_import support csharp and typescript, not {parsed.Language}.");
        var text = Encoding.UTF8.GetString(current.Span);
        var cr = IndentationDetector.LineEnding(current.Span) == "\r\n" ? "\r" : string.Empty;
        var lines = text.Split('\n').ToList();
        var (index, count, changed) = parsed.Language == "csharp" ? EnsureCSharp(lines, cr) : EnsureImport(lines, cr);
        var start = 0;
        for (var i = 0; i < index; i++) start += Encoding.UTF8.GetByteCount(lines[i]) + 1;
        var end = start;
        for (var i = index; i < index + count; i++) end += Encoding.UTF8.GetByteCount(lines[i]) + 1;
        end -= 1 + (lines[index + count - 1].EndsWith('\r') ? 1 : 0);
        var range = new NodeRange(start, end, index + 1, index + count);
        return changed
            ? new EditResult { Bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines)), Range = range }
            : new EditResult { Bytes = current.ToArray(), Range = range, Applied = false };
    }

    private (int Index, int Count, bool Changed) EnsureCSharp(List<string> lines, string cr)
    {
        var present = lines.FindIndex(l => PlainUsing().Match(l.TrimEnd('\r')) is { Success: true } m && m.Groups[1].Value == name);
        if (present >= 0) return (present, 1, false);

        var first = lines.FindIndex(IsTopLevelUsing);
        if (first < 0) return CreateBlock(lines, $"using {name};", cr);

        var last = first;
        while (last + 1 < lines.Count && IsTopLevelUsing(lines[last + 1])) last++;
        var existing = new List<string>();
        for (var i = first; i <= last; i++)
            if (PlainUsing().Match(lines[i].TrimEnd('\r')) is { Success: true } m) existing.Add(m.Groups[1].Value);
        var comparer = UsingOrderDetector.Detect(existing);
        var at = last + 1;
        for (var i = first; i <= last; i++)
        {
            if (PlainUsing().Match(lines[i].TrimEnd('\r')) is not { Success: true } m) continue;
            if (comparer.Compare(m.Groups[1].Value, name) > 0)
            {
                at = i;
                break;
            }
        }

        InsertLine(lines, at, $"using {name};", cr);
        return (at, 1, true);
    }

    private static void InsertLine(List<string> lines, int at, string line, string cr)
    {
        if (at == lines.Count)
        {
            lines[at - 1] += cr;
            lines.Insert(at, line);
        }
        else
            lines.Insert(at, line + cr);
    }

    private static bool IsTopLevelUsing(string line)
    {
        var t = line.TrimEnd('\r');
        return t.StartsWith("using ", StringComparison.Ordinal) && t.EndsWith(';') && !t.Contains('=', StringComparison.Ordinal) && !t.Contains('(', StringComparison.Ordinal);
    }

    private (int Index, int Count, bool Changed) EnsureImport(List<string> lines, string cr)
    {
        var from = module ?? throw new InvalidOperationException("A module path is required for an import.");
        var first = lines.FindIndex(l => SingleLineImport().IsMatch(l.TrimEnd('\r')));
        var quote = "'";
        var semicolon = ";";
        if (first >= 0)
        {
            var sample = lines[first].TrimEnd('\r');
            if (sample.Contains('"', StringComparison.Ordinal) && !sample.Contains('\'', StringComparison.Ordinal)) quote = "\"";
            if (!sample.TrimEnd().EndsWith(';')) semicolon = string.Empty;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var span = 1;
            var m = NamedImport().Match(lines[i].TrimEnd('\r'));
            if (!m.Success && OpenNamedImport().IsMatch(lines[i].TrimEnd('\r')))
            {
                var close = lines.FindIndex(i + 1, l => l.Contains('}', StringComparison.Ordinal));
                if (close > 0)
                {
                    span = close - i + 1;
                    m = NamedImport().Match(string.Join('\n', lines.GetRange(i, span).Select(l => l.TrimEnd('\r'))));
                }
            }

            if (!m.Success || m.Groups[3].Value != from) continue;
            var bindings = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (bindings.Contains(name)) return (i, span, false);
            if (span > 1)
            {
                if (!AppendBinding(lines, i, span)) continue;
                return (i, span + 1, true);
            }

            bindings.Add(name);
            bindings.Sort(string.CompareOrdinal);
            var typeKeyword = lines[i].TrimStart().StartsWith("import type", StringComparison.Ordinal) ? "type " : string.Empty;
            var ending = lines[i].EndsWith('\r') ? "\r" : string.Empty;
            lines[i] = $"import {typeKeyword}{{ {string.Join(", ", bindings)} }} from {m.Groups[2].Value}{from}{m.Groups[2].Value}{semicolon}{ending}";
            return (i, 1, true);
        }

        var line = $"import {{ {name} }} from {quote}{from}{quote}{semicolon}";
        if (first < 0) return CreateBlock(lines, line, cr);

        var at = first;
        var lastMatched = first;
        var placed = false;
        for (var i = first; i < lines.Count && lines[i].StartsWith("import", StringComparison.Ordinal); i++)
        {
            var t = lines[i].TrimEnd('\r');
            if (!SingleLineImport().IsMatch(t)) continue;
            lastMatched = i;
            var mod = ModuleOf().Match(t);
            var other = mod.Groups[1].Success ? mod.Groups[1].Value : mod.Groups[2].Value;
            if (!placed && string.CompareOrdinal(other, from) > 0)
            {
                at = i;
                placed = true;
            }
        }

        if (!placed) at = lastMatched + 1;
        InsertLine(lines, at, line, cr);
        return (at, 1, true);
    }

    private bool AppendBinding(List<string> lines, int import, int span)
    {
        var last = lines.FindLastIndex(import + span - 2, span - 2, l => l.Trim().Length > 0);
        if (last <= import) return false;
        var text = lines[last].TrimEnd('\r');
        var ending = lines[last].EndsWith('\r') ? "\r" : string.Empty;
        var comma = text.TrimEnd().EndsWith(',');
        var indent = text[..(text.Length - text.TrimStart().Length)];
        if (!comma) lines[last] = text + "," + ending;
        lines.Insert(last + 1, $"{indent}{name}{(comma ? "," : string.Empty)}{ending}");
        return true;
    }

    private static (int Index, int Count, bool Changed) CreateBlock(List<string> lines, string line, string cr)
    {
        var at = 0;
        while (at < lines.Count && IsHeader(lines[at])) at++;
        var globals = lines.FindLastIndex(l => l.StartsWith("global using ", StringComparison.Ordinal));
        var afterGlobals = globals >= at;
        if (afterGlobals) at = globals + 1;
        var block = new List<string>();
        if (at == lines.Count)
        {
            lines[at - 1] += cr;
            if (afterGlobals) block.Add(cr);
            block.Add(line);
        }
        else
        {
            if (afterGlobals) block.Add(cr);
            block.Add(line + cr);
            if (lines[at].Trim().Length > 0) block.Add(cr);
        }

        lines.InsertRange(at, block);
        return (at + (afterGlobals ? 1 : 0), 1, true);
    }

    private static bool IsHeader(string line)
    {
        var t = line.Trim();
        return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)
            || t.StartsWith('*');
    }
}
