using System.Text.Json;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The third text shape of a tool call: a JSON envelope written as plain text. Small
/// models served by Ollama or llama.cpp emit it when the server's own tool-call parser
/// gives up on their output (measured 2026-09-11 on the README quickstart, llama3.2:1b
/// under Ollama 0.34.0 on a GitHub runner: the whole envelope came back as the assistant
/// text, the loop took it for the final answer and no tool ran). Accepted, anywhere in
/// the response, fenced or not, one object or an array of them:
/// <code>
/// {"name": "file_write", "parameters": {...}}
/// {"type": "function", "function": {"name": "file_write", "arguments": {...}}}
/// {"type": "function", "function": "file_write", "parameters": {...}}
/// {"tool_calls": [ ...any of the above... ]}
/// </code>
/// A raw line break inside a string -- the usual defect of a hand-written envelope -- is
/// repaired before parsing; anything else malformed is not a tool call, and the loop
/// decides what to do with an answer that looks like one but cannot be executed.
/// </summary>
internal static partial class ToolCallTextParser
{
    private static readonly JsonDocumentOptions s_lenientJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses JSON tool-call envelopes out of a text response. Empty when the text holds
    /// no object that reads as one.
    /// </summary>
    internal static List<ParsedToolCall> ParseJsonEnvelopes(string response)
    {
        var results = new List<ParsedToolCall>();
        foreach (var block in TopLevelJsonBlocks(response))
        {
            var document = TryParseJson(block) ?? TryParseJson(EscapeRawLineBreaksInStrings(block));
            if (document is null)
                continue;
            using (document)
            {
                CollectEnvelopes(document.RootElement, block, results);
            }
        }
        return results;
    }

    /// <summary>
    /// True when the text is shaped like a tool-call attempt -- a JSON object or array that
    /// names one of the tools with the envelope vocabulary, or a [TOOL_CALL] / &lt;invoke&gt;
    /// marker -- whether or not it parses.
    /// The loop uses it to refuse such a text as a final answer.
    /// </summary>
    internal static bool LooksLikeToolCallAttempt(string response, IEnumerable<string> toolNames)
    {
        var text = StripFences(response).Trim();
        if (text.Length == 0)
            return false;
        // JSON that names one of the agent's tools AND carries the envelope vocabulary: a
        // JSON deliverable that merely has a "parameters" key is not a tool call.
        if (text[0] is '{' or '[')
            return (text.Contains("\"parameters\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"arguments\"", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("\"function\"", StringComparison.OrdinalIgnoreCase))
                && toolNames.Any(name => text.Contains($"\"{name}\"", StringComparison.OrdinalIgnoreCase));
        return text.Contains("[TOOL_CALL]", StringComparison.Ordinal)
            || text.Contains("<invoke ", StringComparison.Ordinal);
    }

    private static void CollectEnvelopes(JsonElement element, string block, List<ParsedToolCall> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectEnvelopes(item, block, results);
                break;
            case JsonValueKind.Object:
                if (TryGetProperty(element, "tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                {
                    CollectEnvelopes(calls, block, results);
                    break;
                }
                var call = ToParsedToolCall(element, block);
                if (call is not null)
                    results.Add(call);
                break;
        }
    }

    private static ParsedToolCall? ToParsedToolCall(JsonElement envelope, string block)
    {
        var body = envelope;
        string? name = null;
        if (TryGetProperty(envelope, "function", out var function))
        {
            if (function.ValueKind == JsonValueKind.Object)
                body = function;
            else if (function.ValueKind == JsonValueKind.String)
                name = function.GetString();
        }
        if (name is null && TryGetProperty(body, "name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!TryGetProperty(body, "parameters", out var args) && !TryGetProperty(body, "arguments", out args)
            && !TryGetProperty(envelope, "parameters", out args) && !TryGetProperty(envelope, "arguments", out args))
            return null;

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (args.ValueKind == JsonValueKind.String)
        {
            var nested = TryParseJson(args.GetString() ?? string.Empty);
            if (nested is null || nested.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            using (nested)
                args = nested.RootElement.Clone();
        }
        if (args.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in args.EnumerateObject())
            parameters[NormalizeParameterName(property.Name)] = property.Value.Clone();
        return new ParsedToolCall(name.Trim(), parameters, block);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static JsonDocument? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return JsonDocument.Parse(text, s_lenientJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every balanced top-level <c>{...}</c> or <c>[...]</c> span of the text, fences removed.</summary>
    private static IEnumerable<string> TopLevelJsonBlocks(string response)
    {
        var text = StripFences(response);
        var depth = 0;
        var start = -1;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"' when depth > 0:
                    inString = true;
                    break;
                case '{' or '[':
                    if (depth++ == 0) start = i;
                    break;
                case '}' or ']':
                    if (depth > 0 && --depth == 0 && start >= 0)
                    {
                        yield return text[start..(i + 1)];
                        start = -1;
                    }
                    break;
            }
        }
        // An unterminated string swallowed the closing brace: retry the tail without
        // string tracking, so a raw quote inside a value does not hide the whole block.
        if (depth > 0 && start >= 0 && inString)
        {
            var close = text.LastIndexOf(text[start] == '{' ? '}' : ']');
            if (close > start)
                yield return text[start..(close + 1)];
        }
    }

    private static string StripFences(string response) =>
        response.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal);

    /// <summary>Escapes the raw line breaks a model writes inside JSON string values.</summary>
    internal static string EscapeRawLineBreaksInStrings(string json)
    {
        var builder = new System.Text.StringBuilder(json.Length + 16);
        var inString = false;
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < json.Length)
                {
                    builder.Append(c).Append(json[++i]);
                    continue;
                }
                if (c == '"') inString = false;
                else if (c == '\n') { builder.Append("\\n"); continue; }
                else if (c == '\r') { builder.Append("\\r"); continue; }
                else if (c == '\t') { builder.Append("\\t"); continue; }
            }
            else if (c == '"')
            {
                inString = true;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
