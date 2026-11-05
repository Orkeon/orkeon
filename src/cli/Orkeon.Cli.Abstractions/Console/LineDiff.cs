namespace Orkeon.Cli.Abstractions.Console;

/// <summary>What a row of a diff shows.</summary>
public enum DiffRowKind
{
    /// <summary>A line both sides hold.</summary>
    Context,

    /// <summary>A line only the new side holds.</summary>
    Added,

    /// <summary>A line only the old side holds.</summary>
    Removed,

    /// <summary>Lines left out between two changes.</summary>
    Gap,
}

/// <summary>A row of a diff.</summary>
/// <param name="Kind">What the row shows.</param>
/// <param name="OldLine">The line's number on the old side, from 1; null when it has none there.</param>
/// <param name="NewLine">The line's number on the new side, from 1; null when it has none there.</param>
/// <param name="Text">The line, with no line break.</param>
public readonly record struct DiffRow(DiffRowKind Kind, int? OldLine, int? NewLine, string Text);

/// <summary>What a tool call changed in a file, for display only.</summary>
/// <param name="Path">The file.</param>
/// <param name="Rows">The changes and the lines around them.</param>
public sealed record ToolCallDiff(string Path, IReadOnlyList<DiffRow> Rows)
{
    /// <summary>How many lines the call added.</summary>
    public int Added => Rows.Count(r => r.Kind == DiffRowKind.Added);

    /// <summary>How many lines the call removed.</summary>
    public int Removed => Rows.Count(r => r.Kind == DiffRowKind.Removed);
}

/// <summary>Pure comparison of two texts, by lines, and of two token lists.</summary>
public static class LineDiff
{
    // Past this many cells a table costs more than the display is worth: the part that differs reads as replaced.
    private const long MaxCells = 1_000_000;

    /// <summary>
    /// Marks what two lists have in common, in order, after what they share at both ends has been set aside.
    /// </summary>
    /// <returns>For each side, whether each item is part of what they share.</returns>
    public static (bool[] Left, bool[] Right) Common(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var keptLeft = new bool[left.Count];
        var keptRight = new bool[right.Count];

        var start = 0;
        while (start < left.Count && start < right.Count && left[start] == right[start])
        {
            keptLeft[start] = keptRight[start] = true;
            start++;
        }
        var endLeft = left.Count;
        var endRight = right.Count;
        while (endLeft > start && endRight > start && left[endLeft - 1] == right[endRight - 1])
        {
            endLeft--;
            endRight--;
            keptLeft[endLeft] = keptRight[endRight] = true;
        }

        var rows = endLeft - start;
        var columns = endRight - start;
        if (rows == 0 || columns == 0 || (long)rows * columns > MaxCells) return (keptLeft, keptRight);

        // table[i * (columns + 1) + j]: how much the rest of each side shares from i and j.
        var table = new int[(rows + 1) * (columns + 1)];
        for (var i = rows - 1; i >= 0; i--)
        {
            for (var j = columns - 1; j >= 0; j--)
            {
                table[(i * (columns + 1)) + j] = left[start + i] == right[start + j]
                    ? table[((i + 1) * (columns + 1)) + j + 1] + 1
                    : Math.Max(table[((i + 1) * (columns + 1)) + j], table[(i * (columns + 1)) + j + 1]);
            }
        }

        for (int i = 0, j = 0; i < rows && j < columns;)
        {
            if (left[start + i] == right[start + j])
            {
                keptLeft[start + i++] = true;
                keptRight[start + j++] = true;
            }
            else if (table[((i + 1) * (columns + 1)) + j] >= table[(i * (columns + 1)) + j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return (keptLeft, keptRight);
    }

    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/>, with the lines around them.</summary>
    /// <param name="before">The old text.</param>
    /// <param name="after">The new text.</param>
    /// <param name="context">How many unchanged lines to keep on each side of a change.</param>
    public static IReadOnlyList<DiffRow> Compute(string before, string after, int context = 3)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var oldLines = Lines(before);
        var newLines = Lines(after);
        var (keptOld, keptNew) = Common(oldLines, newLines);

        var all = new List<DiffRow>(oldLines.Length + newLines.Length);
        int o = 0, n = 0;
        while (o < oldLines.Length || n < newLines.Length)
        {
            if (o < oldLines.Length && !keptOld[o])
            {
                all.Add(new DiffRow(DiffRowKind.Removed, o + 1, null, oldLines[o]));
                o++;
            }
            else if (n < newLines.Length && !keptNew[n])
            {
                all.Add(new DiffRow(DiffRowKind.Added, null, n + 1, newLines[n]));
                n++;
            }
            else
            {
                all.Add(new DiffRow(DiffRowKind.Context, o + 1, n + 1, newLines[n]));
                o++;
                n++;
            }
        }
        return WithContext(all, context);
    }

    private static string[] Lines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Length == 0) return [];
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        return normalized.Split('\n');
    }

    private static List<DiffRow> WithContext(List<DiffRow> all, int context)
    {
        var keep = new bool[all.Count];
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Kind == DiffRowKind.Context) continue;
            for (var k = Math.Max(0, i - context); k <= Math.Min(all.Count - 1, i + context); k++)
                keep[k] = true;
        }

        var rows = new List<DiffRow>();
        var skipping = false;
        for (var i = 0; i < all.Count; i++)
        {
            if (keep[i])
            {
                if (skipping && rows.Count > 0) rows.Add(new DiffRow(DiffRowKind.Gap, null, null, string.Empty));
                skipping = false;
                rows.Add(all[i]);
            }
            else
            {
                skipping = true;
            }
        }
        return rows;
    }
}
