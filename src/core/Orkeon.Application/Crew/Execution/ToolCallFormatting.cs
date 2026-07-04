namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Stateless formatting helpers for tool call arguments and results, shared by the
/// execution loops and the tool dispatcher. Extracted verbatim from
/// <see cref="ExecutionOrchestrator"/> (R4.1).
/// </summary>
internal static class ToolCallFormatting
{
    private static readonly System.Text.Json.JsonSerializerOptions s_utf8JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string FormatToolArgs(Dictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return "";
        var parts = parameters.Select(entry =>
            $"{entry.Key}=\"{Truncate(FormatArgValue(entry.Value), 120)}\"");
        return string.Join(", ", parts);
    }

    private static string FormatArgValue(object? value)
    {
        if (value is null) return "";
        if (value is string s) return s;
        if (value is System.Collections.IDictionary || value is System.Collections.IEnumerable)
            return System.Text.Json.JsonSerializer.Serialize(value, s_utf8JsonOptions);
        return value.ToString() ?? "";
    }

    internal static string FormatResult(object? result)
    {
        if (result is null) return "(empty)";
        if (result is string s) return s;
        if (result is System.Collections.IDictionary || result is System.Collections.IEnumerable and not string)
            return System.Text.Json.JsonSerializer.Serialize(result, s_utf8JsonOptions);
        return result.ToString() ?? "(empty)";
    }

    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return string.Concat(text.AsSpan(0, maxLength), "...");
    }
}
