using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Parses tool calls from LLM text responses using regex-based extraction.
/// Supports two text formats:
/// <list type="number">
///   <item>[TOOL_CALL]{tool => "name", args => {--key "value"}}[/TOOL_CALL]</item>
///   <item>XML-style: &lt;invoke name="tool"&gt; with inline attrs or &lt;parameter&gt; children</item>
/// </list>
/// This is the fallback parser used when the LLM provider does not support native tool calling.
/// </summary>
public sealed partial class TextFallbackToolCallParser : IToolCallParser
{
    private readonly ILogger<TextFallbackToolCallParser> _logger;

    // ── Regex patterns (source-generated, ReDoS-protected via matchTimeoutMilliseconds) ──

    [GeneratedRegex(
        @"\[TOOL_CALL\]\s*\{tool\s*=>\s*""(?<toolName>[^""]+)""\s*,\s*args\s*=>\s*\{(?<args>[^}]*)\}\s*\}\s*\[/TOOL_CALL\]",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(
        @"--(?<key>\S+)\s+""(?<value>[^""]*)""|--(?<key2>\S+)\s+(?<value2>\S+)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ArgPairRegex();

    [GeneratedRegex(
        @"<invoke\s+name=""(?<toolName>[^""]+)""(?<attrs>[^>]*)(?:/>|>(?<body>[\s\S]*?)</invoke>)",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex XmlInvokeRegex();

    [GeneratedRegex(
        @"(?<key>\w+)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex XmlInlineAttrRegex();

    [GeneratedRegex(
        @"<parameter\s+name=""(?<key>[^""]+)"">(?<value>[\s\S]*?)</parameter>",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex XmlParameterRegex();

    // ── Internal record (separate from Application's ParsedToolCall) ────

    private sealed record TextParsedToolCall(string ToolName, Dictionary<string, object?> Parameters, string RawBlock);

    // ── Constructor ─────────────────────────────────────────────────────

    /// <summary>Initializes a new instance of <see cref="TextFallbackToolCallParser"/>.</summary>
    /// <param name="logger">The logger.</param>
    public TextFallbackToolCallParser(ILogger<TextFallbackToolCallParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IToolCallParser implementation ──────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<ParsedToolCall> ParseToolCalls(JsonElement responseBody)
    {
        var text = ExtractTextContent(responseBody);
        if (string.IsNullOrEmpty(text))
            return [];

        return ParseToolCallsFromText(text);
    }

    /// <inheritdoc />
    public object FormatToolResult(ParsedToolCall toolCall, string result, bool success)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        return new Dictionary<string, object>
        {
            ["role"] = "user",
            ["content"] = $"Tool {toolCall.ToolName} result: {(success ? result : $"Error: {result}")}"
        };
    }

    /// <inheritdoc />
    public object FormatAssistantToolCallMessage(JsonElement responseBody)
    {
        var text = ExtractTextContent(responseBody) ?? "";
        return new Dictionary<string, object>
        {
            ["role"] = "assistant",
            ["content"] = text
        };
    }

    // ── Text extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts the textual content from a raw LLM JSON response.
    /// Tries OpenAI format (choices[0].message.content) and Anthropic format (content[0].text).
    /// </summary>
    private static string? ExtractTextContent(JsonElement responseBody)
    {
        // Try OpenAI format: choices[0].message.content
        if (responseBody.TryGetProperty("choices", out var choices))
        {
            var firstChoice = choices.EnumerateArray().FirstOrDefault();
            if (firstChoice.ValueKind != JsonValueKind.Undefined &&
                firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        // Try Anthropic format: content[0].text
        if (responseBody.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentArray.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    item.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    // ── Conversion from internal to Application ParsedToolCall ──────────

    private List<ParsedToolCall> ParseToolCallsFromText(string text)
    {
        var internalCalls = ParseToolCallBlocks(text);

        if (internalCalls.Count > 0)
        {
            LogExtractedToolCalls(internalCalls.Count);
        }

        return internalCalls.Select((call, index) =>
            new ParsedToolCall(
                $"text_call_{index}",
                call.ToolName,
                call.Parameters))
            .ToList();
    }

    // ── Regex-based parsing (migrated from ExecutionOrchestrator) ────────

    /// <summary>
    /// Parses all tool call blocks from an LLM text response.
    /// Supports two formats, tried in order:
    /// 1. [TOOL_CALL]{tool => "name", args => {--key "value"}}[/TOOL_CALL]
    /// 2. XML-style: &lt;invoke name="tool"&gt; with inline attrs or &lt;parameter&gt; children
    /// </summary>
    private static List<TextParsedToolCall> ParseToolCallBlocks(string response)
    {
        var results = ParseStructuredToolCallBlocks(response);

        // Fallback: if no [TOOL_CALL] blocks found, try XML <invoke> format
        if (results.Count == 0)
        {
            results.AddRange(ParseXmlToolCallBlocks(response));
        }

        return results;
    }

    /// <summary>
    /// Parses the structured [TOOL_CALL]{tool => ..., args => {...}}[/TOOL_CALL] format.
    /// </summary>
    private static List<TextParsedToolCall> ParseStructuredToolCallBlocks(string response)
    {
        return ToolCallBlockRegex()
            .Matches(response)
            .Cast<Match>()
            .Select(BuildStructuredCall)
            .ToList();
    }

    /// <summary>Builds a <see cref="TextParsedToolCall"/> from a matched [TOOL_CALL] block.</summary>
    private static TextParsedToolCall BuildStructuredCall(Match match)
    {
        var toolName = match.Groups["toolName"].Value.Trim();
        var argsText = match.Groups["args"].Value;
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in ArgPairRegex()
            .Matches(argsText)
            .Cast<Match>()
            .Select(ExtractArgPair))
        {
            var normalizedKey = NormalizeParameterName(key);
            parameters[normalizedKey] = SanitizeParameterValue(normalizedKey, value);
        }

        return new TextParsedToolCall(toolName, parameters, match.Value);
    }

    /// <summary>Extracts a (key, value) pair from an ArgPairRegex match, handling both quoted and unquoted variants.</summary>
    private static (string Key, string Value) ExtractArgPair(Match argMatch)
    {
        var key = argMatch.Groups["key"].Success ? argMatch.Groups["key"].Value : argMatch.Groups["key2"].Value;
        var value = argMatch.Groups["value"].Success ? argMatch.Groups["value"].Value : argMatch.Groups["value2"].Value;
        return (key, value);
    }

    /// <summary>
    /// Parses XML-style tool call blocks from an LLM text response.
    /// </summary>
    private static List<TextParsedToolCall> ParseXmlToolCallBlocks(string response)
    {
        var results = new List<TextParsedToolCall>();

        foreach (Match match in XmlInvokeRegex().Matches(response))
        {
            var toolName = match.Groups["toolName"].Value.Trim();
            if (string.IsNullOrEmpty(toolName))
                continue;

            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            PopulateFromInlineAttributes(parameters, match.Groups["attrs"].Value);
            PopulateFromParameterBody(parameters, match.Groups["body"].Value);

            results.Add(new TextParsedToolCall(toolName, parameters, match.Value));
        }

        return results;
    }

    /// <summary>Populates <paramref name="parameters"/> from inline XML attributes, skipping the reserved <c>name</c> attribute.</summary>
    private static void PopulateFromInlineAttributes(Dictionary<string, object?> parameters, string attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return;

        foreach (var (key, value) in XmlInlineAttrRegex()
            .Matches(attrs)
            .Cast<Match>()
            .Select(m => (Key: m.Groups["key"].Value, Value: m.Groups["value"].Value)))
        {
            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalizedKey = NormalizeParameterName(key);
            parameters[normalizedKey] = SanitizeParameterValue(normalizedKey, value);
        }
    }

    /// <summary>Populates <paramref name="parameters"/> from <c>&lt;parameter name="..."&gt;...&lt;/parameter&gt;</c> children inside an invoke body.</summary>
    private static void PopulateFromParameterBody(Dictionary<string, object?> parameters, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        foreach (var (key, value) in XmlParameterRegex()
            .Matches(body)
            .Cast<Match>()
            .Select(m => (Key: m.Groups["key"].Value.Trim(), Value: m.Groups["value"].Value.Trim())))
        {
            var normalizedKey = NormalizeParameterName(key);
            parameters[normalizedKey] = SanitizeParameterValue(normalizedKey, value);
        }
    }

    // ── Parameter normalization and sanitization ────────────────────────

    /// <summary>
    /// Normalizes LLM-generated parameter names to match tool schema expectations.
    /// Handles common mismatches like css_selector -> selector, etc.
    /// </summary>
    internal static string NormalizeParameterName(string rawName)
    {
        var name = rawName.TrimStart('-');

#pragma warning disable CA1308 // lowercase is the required normalized form matched by the switch, not a comparison normalization
        return name.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "css_selector" => "selector",
            "xpath_selector" => "selector",
            "query" => "query",
            "file_path" => "path",
            "filepath" => "path",
            "input_file" => "path",
            "input" => "path",
            "files" => "path",
            "file" => "path",
            "text" => "content",
            "body" => "content",
            "data" => "content",
            "directory" => "path",
            "dir" => "path",
            "folder" => "path",
            "dir_path" => "path",
            "directory_path" => "path",
            "recurse" => "recursive",
            "include_subdirs" => "recursive",
            _ => name
        };
    }

    /// <summary>
    /// Sanitizes a parameter value to fix common LLM formatting issues.
    /// Handles: trailing commas/semicolons, JSON array wrapping, double-quoting,
    /// and type coercion for booleans and integers.
    /// </summary>
    internal static object SanitizeParameterValue(string key, object value)
    {
        if (value is not string strValue || string.IsNullOrEmpty(strValue))
            return value;

        // Strip trailing commas, semicolons, and colons — common LLM formatting artifacts
        // e.g. "true," → "true", "100;" → "100"
        var trimmed = strValue.Trim().TrimEnd(',', ';', ':');

        // Fix JSON array-wrapped values: ["some/path"] -> some/path
        if (TryUnwrapJsonArray(trimmed, out var unwrapped))
            return unwrapped;

        // Fix double-quoted paths: "\"some/path\"" -> some/path
        trimmed = StripSurroundingQuotes(trimmed);

        // Unescape common LLM escape sequences: literal \n → newline, \t → tab, \\ → backslash
        trimmed = UnescapeLlmText(trimmed);

        // Type coercion: boolean and integer strings → actual primitives
        return CoerceToPrimitive(trimmed);
    }

    /// <summary>
    /// Attempts to unwrap a value that looks like a JSON array containing a single string element.
    /// Returns <c>true</c> if the input was detected as an array and <paramref name="unwrapped"/> holds the extracted value.
    /// </summary>
    private static bool TryUnwrapJsonArray(string trimmed, out string unwrapped)
    {
        unwrapped = trimmed;

        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(trimmed);
            if (parsed is { Length: 1 })
            {
                unwrapped = parsed[0];
                return true;
            }
        }
        catch (JsonException)
        {
            var inner = trimmed[1..^1].Trim().Trim('"');
            if (!string.IsNullOrEmpty(inner))
            {
                unwrapped = inner;
                return true;
            }
        }

        return false;
    }

    /// <summary>Strips a single pair of matching surrounding double quotes from <paramref name="value"/>, if present.</summary>
    private static string StripSurroundingQuotes(string value)
    {
        if (value.Length > 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];
        return value;
    }

    /// <summary>
    /// Coerces a trimmed string to a <see cref="bool"/> or <see cref="int"/> when the literal is unambiguous,
    /// otherwise returns the original string.
    /// </summary>
    private static object CoerceToPrimitive(string trimmed)
    {
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        if (int.TryParse(
                trimmed,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var intValue))
        {
            return intValue;
        }

        return trimmed;
    }

    /// <summary>
    /// Unescapes common LLM text escape sequences.
    /// LLMs frequently emit literal <c>\n</c> (two characters: backslash + n) instead of actual newline characters.
    /// </summary>
    internal static string UnescapeLlmText(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\\', StringComparison.Ordinal))
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                switch (text[i + 1])
                {
                    case 'n':
                        sb.Append('\n');
                        i++;
                        break;
                    case 't':
                        sb.Append('\t');
                        i++;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        break;
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    default:
                        sb.Append(text[i]);
                        break;
                }
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "TextFallbackToolCallParser extracted {Count} tool call(s) from text response.")]
    private partial void LogExtractedToolCalls(int count);
}
