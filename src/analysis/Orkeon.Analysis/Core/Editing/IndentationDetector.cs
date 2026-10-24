using System.Text;

namespace Orkeon.Analysis.Core.Editing;

internal static class IndentationDetector
{
    /// <summary>One indentation level of the file: a tab when tab-indented lines outnumber space-indented ones, else the smallest space indent (four by default).</summary>
    public static string Unit(ReadOnlySpan<byte> file)
    {
        var tabs = 0;
        var spaces = 0;
        var smallest = int.MaxValue;
        foreach (var range in file.Split((byte)'\n'))
        {
            var line = file[range];
            if (line.Length == 0) continue;
            if (line[0] == (byte)'\t') tabs++;
            if (line[0] != (byte)' ') continue;
            var width = line.Length - line.TrimStart((byte)' ').Length;
            var rest = line[width..];
            if (rest.Length == 0 || rest[0] == (byte)'\r' || rest[0] == (byte)'*') continue;
            spaces++;
            smallest = Math.Min(smallest, width);
        }

        if (tabs > spaces) return "\t";
        return new string(' ', smallest == int.MaxValue ? 4 : smallest);
    }

    /// <summary>The leading whitespace of the line holding <paramref name="index"/>.</summary>
    public static string IndentOf(ReadOnlySpan<byte> file, int index)
    {
        var start = file[..index].LastIndexOf((byte)'\n') + 1;
        var end = start;
        while (end < file.Length && file[end] is (byte)' ' or (byte)'\t') end++;
        return Encoding.UTF8.GetString(file[start..end]);
    }

    public static string LineEnding(ReadOnlySpan<byte> file)
    {
        var crlf = file.Count("\r\n"u8);
        return crlf > file.Count((byte)'\n') - crlf ? "\r\n" : "\n";
    }
}
