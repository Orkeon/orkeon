using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// Tolerant parser for LLM-generated query variants (guide §6.6). Accepts, in
/// order of preference: a JSON string array anywhere in the text (including
/// inside a fenced code block), then plain lines — numbered (<c>1.</c>,
/// <c>2)</c>, <c>3:</c>…), bulleted (<c>-</c>, <c>*</c>, <c>•</c>) or bare.
/// Wrapping quotes are stripped, empty lines and preamble lines ending with
/// <c>:</c> are dropped. Never throws on malformed output — an unusable
/// response simply yields an empty list.
/// </summary>
internal static partial class QueryVariantParser
{
    /// <summary>Extracts the candidate variants from <paramref name="responseText"/>.</summary>
    internal static List<string> Parse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return [];
        }

        var fromJson = TryParseJsonStringArray(responseText);
        if (fromJson is { Count: > 0 })
        {
            return CleanAll(fromJson);
        }

        return CleanAll(responseText.Split('\n'));
    }

    private static List<string>? TryParseJsonStringArray(string text)
    {
        var start = text.IndexOf('[', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = text.IndexOf(']', start + 1);
            if (end < 0)
            {
#pragma warning disable S1168 // null signals a parse failure so the caller can fall back
                return null;
#pragma warning restore S1168
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(text[start..(end + 1)]);
                if (parsed is { Count: > 0 })
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Not a valid JSON string array at this position — keep scanning.
            }

            start = text.IndexOf('[', end + 1);
        }

        return null;
    }

    private static List<string> CleanAll(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            var cleaned = CleanLine(line);
            if (cleaned.Length > 0)
            {
                result.Add(cleaned);
            }
        }

        return result;
    }

    /// <summary>
    /// Strips list decorations (numbering, bullets), wrapping quotes and code
    /// fences from one line; returns an empty string for unusable lines
    /// (blank, fences, preamble ending with <c>:</c>).
    /// </summary>
    private static string CleanLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith("```", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        text = ListDecorationRegex().Replace(text, string.Empty).Trim().TrimEnd(',').Trim();
        text = StripWrappingQuotes(text);

        // A line ending with ':' is a preamble ("Here are 3 variants:"), not a variant.
        if (text.Length == 0 || text.EndsWith(':'))
        {
            return string.Empty;
        }

        return text;
    }

    private static string StripWrappingQuotes(string text)
    {
        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"')
                || (text[0] == '\'' && text[^1] == '\'')
                || (text[0] == '“' && text[^1] == '”')))
        {
            return text[1..^1].Trim();
        }

        return text;
    }

    // "1. ", "2) ", "3] ", "4: ", "5 - " … (max 2 digits so years stay intact) or "-", "*", "•" bullets.
    [GeneratedRegex(@"^(?:\d{1,2}\s*[.)\]:—–-]|[-*•‣▪])\s*")]
    private static partial Regex ListDecorationRegex();
}
