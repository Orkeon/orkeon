using System.Globalization;
using System.Text.Json;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Tolerant JSON extraction shared by the corrective LLM components
/// (<see cref="LlmRetrievalEvaluator"/>, <see cref="LlmGroundednessChecker"/>):
/// finds the first parseable, balanced JSON object anywhere in a model response
/// (code fences and prose tolerated) and reads properties case-insensitively
/// with type coercion. Never throws on malformed input — callers fall back to
/// their safe default instead.
/// </summary>
internal static class TolerantJsonReader
{
    /// <summary>
    /// Extracts the first balanced, parseable JSON object found in
    /// <paramref name="text"/>; <c>null</c> when there is none.
    /// </summary>
    public static JsonDocument? ExtractFirstObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{', StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = FindBalancedEnd(text, start);
            if (end > start)
            {
                try
                {
                    var document = JsonDocument.Parse(text[start..(end + 1)]);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                        return document;

                    document.Dispose();
                }
                catch (JsonException)
                {
                    // Not a valid JSON object at this position — keep scanning.
                }
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    /// <summary>Finds <paramref name="element"/>'s property named <paramref name="name"/>, case-insensitively.</summary>
    public static JsonElement? GetPropertyIgnoreCase(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    /// <summary>Reads a string property (case-insensitive name); <c>null</c> when absent or not a string.</summary>
    public static string? GetString(JsonElement element, string name)
    {
        var value = GetPropertyIgnoreCase(element, name);
        return value is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    }

    /// <summary>
    /// Reads a number property tolerantly (case-insensitive name): a JSON number
    /// or a numeric string both parse; <c>null</c> otherwise.
    /// </summary>
    public static double? GetDouble(JsonElement element, string name)
    {
        var value = GetPropertyIgnoreCase(element, name);
        return value is { } v ? CoerceDouble(v) : null;
    }

    /// <summary>
    /// Reads a boolean property tolerantly (case-insensitive name): a JSON
    /// boolean, or the strings <c>true|false|yes|no</c>; <c>null</c> otherwise.
    /// </summary>
    public static bool? GetBool(JsonElement element, string name)
    {
        var value = GetPropertyIgnoreCase(element, name);
        if (value is not { } v)
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString()?.Trim().ToUpperInvariant() switch
            {
                "TRUE" or "YES" or "GROUNDED" => true,
                "FALSE" or "NO" or "UNGROUNDED" => false,
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>Coerces a JSON number or numeric string into a double; <c>null</c> otherwise.</summary>
    public static double? CoerceDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Index (inclusive) of the <c>}</c> closing the object opened at
    /// <paramref name="start"/>, honouring nesting and string literals;
    /// <c>-1</c> when the object never closes.
    /// </summary>
    private static int FindBalancedEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return i;
                    break;
                default:
                    break;
            }
        }

        return -1;
    }
}
