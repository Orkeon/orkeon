using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Pure rendering of a diff into transcript lines: numbers, a painted row per change, the words that changed.</summary>
internal static partial class DiffRenderer
{
    private const string Indent = "  ";
    private const int MaxRows = 80;
    private const int MaxTokens = 600;

    // Below this share of common text a pair of lines is a rewrite, and marking its words would say nothing.
    private const double MinCommonShare = 0.4;

    private readonly record struct WordMarks(IReadOnlyList<(int Start, int Length)> Marked);

    /// <summary>The lines a tool call's change leaves in the transcript, under a line that names the file.</summary>
    public static IReadOnlyList<TranscriptLine> ForCall(ToolCallDiff diff, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(glyphs);
        var lines = new List<TranscriptLine> { Header(diff, glyphs) };
        lines.AddRange(Rows(diff.Rows, glyphs));
        return lines;
    }

    /// <summary>The rows, the longest diffs cut with a line that says how many are left out.</summary>
    public static IReadOnlyList<TranscriptLine> Rows(IReadOnlyList<DiffRow> rows, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(glyphs);
        var shown = rows.Take(MaxRows).ToList();
        var numberWidth = NumberWidth(shown);
        var marks = MarkWords(shown);
        var lines = new List<TranscriptLine>(shown.Count + 1);
        for (var i = 0; i < shown.Count; i++)
            lines.Add(shown[i].Kind == DiffRowKind.Gap
                ? TranscriptLine.InRole(Indent + glyphs.Ellipsis, PaletteRole.Dim)
                : RowLine(shown[i], numberWidth, marks.GetValueOrDefault(i)));
        if (rows.Count > MaxRows)
            lines.Add(TranscriptLine.InRole($"{Indent}{glyphs.Ellipsis} +{rows.Count - MaxRows} lines", PaletteRole.Dim));
        return lines;
    }

    /// <summary>A unified diff as a command prints it. A copy of a whole line gives back the patch's own line. Without a hunk there is no number to add: the lines stay as they are, painted.</summary>
    public static IReadOnlyList<TranscriptLine> Unified(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var source = text.Split('\n');
        var parsed = new List<(DiffRow? Row, string Raw)>(source.Length);
        var state = new HunkState();
        foreach (var raw in source)
            parsed.Add((state.Read(raw), raw));

        var rows = parsed.Where(p => p.Row is not null).Select(p => p.Row!.Value).ToList();
        var numberWidth = NumberWidth(rows);
        var marks = MarkWords(rows);
        var lines = new List<TranscriptLine>(parsed.Count);
        var at = 0;
        foreach (var (row, raw) in parsed)
        {
            if (row is { } shown && numberWidth == 0)
                lines.Add(new TranscriptLine(raw) { RowRole = RowRoleOf(shown.Kind) });
            else if (row is { } numbered)
                lines.Add(RowLine(numbered, numberWidth, marks.GetValueOrDefault(at++)) with { CopySource = raw });
            else
                lines.Add(OtherLine(raw));
        }
        return lines;
    }

    // Line counters of the hunk being read; the left counts are the lines the hunk still announces:
    // until they are read, a header-looking line inside the hunk is a change.
    private sealed class HunkState
    {
        private int _oldLine;
        private int _newLine;
        private int _oldLeft;
        private int _newLeft;

        public DiffRow? Read(string raw)
        {
            var inHunk = raw.StartsWith('+') ? _newLeft > 0 : raw.StartsWith('-') && _oldLeft > 0;
            if ((IsFileHeader(raw) && !inHunk) || raw.StartsWith("@@", StringComparison.Ordinal))
            {
                ReadHeader(raw);
                return null;
            }
            if (raw.StartsWith('+')) return ReadAdded(raw);
            if (raw.StartsWith('-')) return ReadRemoved(raw);
            if (_newLine > 0 && raw.StartsWith(' ')) return ReadContext(raw);
            return null;
        }

        private void ReadHeader(string raw)
        {
            var hunk = Hunk().Match(raw);
            if (!hunk.Success) return;
            _oldLine = int.Parse(hunk.Groups[1].Value, CultureInfo.InvariantCulture);
            _newLine = int.Parse(hunk.Groups[3].Value, CultureInfo.InvariantCulture);
            _oldLeft = Announced(hunk.Groups[2]);
            _newLeft = Announced(hunk.Groups[4]);
        }

        private DiffRow ReadAdded(string raw)
        {
            var row = new DiffRow(DiffRowKind.Added, null, Number(_newLine), raw[1..]);
            if (_newLine > 0) _newLine++;
            _newLeft--;
            return row;
        }

        private DiffRow ReadRemoved(string raw)
        {
            var row = new DiffRow(DiffRowKind.Removed, Number(_oldLine), null, raw[1..]);
            if (_oldLine > 0) _oldLine++;
            _oldLeft--;
            return row;
        }

        private DiffRow ReadContext(string raw)
        {
            var row = new DiffRow(DiffRowKind.Context, Number(_oldLine), Number(_newLine), raw[1..]);
            _oldLine++;
            _newLine++;
            _oldLeft--;
            _newLeft--;
            return row;
        }
    }

    private static int? Number(int line) => line > 0 ? line : null;

    // A hunk's count left out means one line.
    private static int Announced(Group count) => count.Success ? int.Parse(count.Value, CultureInfo.InvariantCulture) : 1;

    private static bool IsFileHeader(string line)
        => line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal);

    private static TranscriptLine OtherLine(string line)
    {
        if (line.StartsWith("@@", StringComparison.Ordinal)) return TranscriptLine.InRole(line, PaletteRole.Code);
        return IsFileHeader(line) ? TranscriptLine.InRole(line, PaletteRole.Dim) : new TranscriptLine(line);
    }

    private static TranscriptLine Header(ToolCallDiff diff, GlyphSet glyphs)
    {
        var head = $"{glyphs.Bullet} ";
        var counts = $" +{diff.Added} -{diff.Removed}";
        var added = counts.IndexOf('-', StringComparison.Ordinal) - 1;
        var at = head.Length + diff.Path.Length;
        return new TranscriptLine(head + diff.Path + counts)
        {
            Runs =
            [
                new TranscriptRun(0, head.Length, PaletteRole.Dim),
                new TranscriptRun(head.Length, diff.Path.Length, PaletteRole.Normal, Bold: true),
                new TranscriptRun(at, added, PaletteRole.Ok),
                new TranscriptRun(at + added, counts.Length - added, PaletteRole.Error),
            ],
        };
    }

    private static int NumberWidth(List<DiffRow> rows)
    {
        var widest = 0;
        foreach (var row in rows)
            widest = Math.Max(widest, row.NewLine ?? row.OldLine ?? 0);
        return widest == 0 ? 0 : widest.ToString(CultureInfo.InvariantCulture).Length;
    }

    private static TranscriptLine RowLine(DiffRow row, int numberWidth, WordMarks marks)
    {
        var number = (row.NewLine ?? row.OldLine)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var numberPart = numberWidth == 0 ? string.Empty : number.PadLeft(numberWidth) + " ";
        var sign = row.Kind switch { DiffRowKind.Added => "+ ", DiffRowKind.Removed => "- ", _ => "  " };
        var head = Indent + numberPart + sign;
        var text = Untabbed(row.Text);

        var runs = new List<TranscriptRun>();
        if (row.Kind == DiffRowKind.Context)
        {
            runs.Add(new TranscriptRun(0, head.Length, PaletteRole.Dim));
        }
        else if (marks.Marked is { } spans)
        {
            var word = row.Kind == DiffRowKind.Added ? PaletteRole.AddedWord : PaletteRole.RemovedWord;
            foreach (var (start, length) in spans)
                runs.Add(new TranscriptRun(head.Length + start, length, word));
        }

        return new TranscriptLine(head + text)
        {
            Runs = runs,
            HangingIndent = head.Length,
            RowRole = RowRoleOf(row.Kind),
        };
    }

    private static PaletteRole? RowRoleOf(DiffRowKind kind) => kind switch
    {
        DiffRowKind.Added => PaletteRole.DiffAdded,
        DiffRowKind.Removed => PaletteRole.DiffRemoved,
        _ => null,
    };

    private static string Untabbed(string text) => text.Replace("\t", "    ", StringComparison.Ordinal);

    // A block of removed lines right before a block of added ones reads line by line: the first with the first.
    private static Dictionary<int, WordMarks> MarkWords(List<DiffRow> rows)
    {
        var marks = new Dictionary<int, WordMarks>();
        var i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Kind != DiffRowKind.Removed)
            {
                i++;
                continue;
            }
            var removed = i;
            while (i < rows.Count && rows[i].Kind == DiffRowKind.Removed) i++;
            var added = i;
            while (i < rows.Count && rows[i].Kind == DiffRowKind.Added) i++;
            for (var k = 0; k < Math.Min(added - removed, i - added); k++)
                MarkPair(rows, removed + k, added + k, marks);
        }
        return marks;
    }

    private static void MarkPair(List<DiffRow> rows, int removed, int added, Dictionary<int, WordMarks> marks)
    {
        var left = Tokens(Untabbed(rows[removed].Text));
        var right = Tokens(Untabbed(rows[added].Text));
        if (left.Count > MaxTokens || right.Count > MaxTokens) return;
        var (keptLeft, keptRight) = LineDiff.Common(left, right);
        var common = left.Where((_, k) => keptLeft[k]).Sum(t => string.IsNullOrWhiteSpace(t) ? 0 : t.Length);
        var longest = Math.Max(left.Sum(t => t.Length), right.Sum(t => t.Length));
        if (longest == 0 || common < longest * MinCommonShare) return;
        marks[removed] = new WordMarks(Spans(left, keptLeft));
        marks[added] = new WordMarks(Spans(right, keptRight));
    }

    private static List<string> Tokens(string text) => [.. Token().Matches(text).Select(m => m.Value)];

    private static List<(int Start, int Length)> Spans(List<string> tokens, bool[] kept)
    {
        var spans = new List<(int Start, int Length)>();
        var at = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!kept[i])
            {
                if (spans.Count > 0 && spans[^1].Start + spans[^1].Length == at)
                    spans[^1] = (spans[^1].Start, spans[^1].Length + tokens[i].Length);
                else
                    spans.Add((at, tokens[i].Length));
            }
            at += tokens[i].Length;
        }
        return spans;
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))?")]
    private static partial Regex Hunk();

    [GeneratedRegex(@"\w+|\s+|[\uD800-\uDBFF][\uDC00-\uDFFF]|.", RegexOptions.Singleline)]
    private static partial Regex Token();
}
