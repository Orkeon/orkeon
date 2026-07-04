using System.Text;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Represents a parsed tool call extracted from LLM text output.
/// </summary>
internal sealed record ParsedToolCall(string ToolName, Dictionary<string, object?> Parameters, string RawBlock);

/// <summary>
/// Stateless text/XML parser for tool call blocks emitted by LLMs that do not use
/// native function calling. Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1).
/// Supports the structured <c>[TOOL_CALL]…[/TOOL_CALL]</c> format and the XML
/// <c>&lt;invoke&gt;</c> format (MiniMax, Anthropic proxies), plus parameter-name
/// normalization, value sanitization and LLM escape-sequence handling.
/// </summary>
internal static partial class ToolCallTextParser
{
    /// <summary>
    /// Regex pattern to extract [TOOL_CALL]...[/TOOL_CALL] blocks from LLM text responses.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"\[TOOL_CALL\]\s*\{tool\s*=>\s*""(?<toolName>[^""]+)""\s*,\s*args\s*=>\s*\{(?<args>[^}]*)\}\s*\}\s*\[/TOOL_CALL\]",
        System.Text.RegularExpressions.RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial System.Text.RegularExpressions.Regex ToolCallBlockRegex();

    /// <summary>
    /// Regex pattern to extract individual --key "value" argument pairs.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"--(?<key>\S+)\s+""(?<value>[^""]*)""|--(?<key2>\S+)\s+(?<value2>\S+)",
        System.Text.RegularExpressions.RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial System.Text.RegularExpressions.Regex ArgPairRegex();

    /// <summary>
    /// Regex to extract XML-style tool calls: &lt;invoke name="tool_name"&gt; with inline attributes or &lt;parameter&gt; children.
    /// Handles formats emitted by MiniMax, Anthropic proxies, and similar providers.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"<invoke\s+name=""(?<toolName>[^""]+)""(?<attrs>[^>]*)(?:/>|>(?<body>[\s\S]*?)</invoke>)",
        System.Text.RegularExpressions.RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial System.Text.RegularExpressions.Regex XmlInvokeRegex();

    /// <summary>
    /// Regex to extract inline attributes from &lt;invoke name="..." key="value"&gt; format.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"(?<key>\w+)\s*=\s*""(?<value>[^""]*)""",
        System.Text.RegularExpressions.RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial System.Text.RegularExpressions.Regex XmlInlineAttrRegex();

    /// <summary>
    /// Regex to extract &lt;parameter name="key"&gt;value&lt;/parameter&gt; elements.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(
        @"<parameter\s+name=""(?<key>[^""]+)"">(?<value>[\s\S]*?)</parameter>",
        System.Text.RegularExpressions.RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial System.Text.RegularExpressions.Regex XmlParameterRegex();

    /// <summary>
    /// Parses all tool call blocks from an LLM text response.
    /// Supports two formats:
    /// 1. [TOOL_CALL]{tool => "name", args => {--key "value"}}[/TOOL_CALL]
    /// 2. XML-style: &lt;invoke name="tool"&gt; with inline attrs or &lt;parameter&gt; children (MiniMax, Anthropic proxies)
    /// </summary>
    internal static List<ParsedToolCall> ParseToolCallBlocks(string response)
    {
        // 1. Try structured [TOOL_CALL] format first
        var results = ToolCallBlockRegex().Matches(response)
            .Select(match => new ParsedToolCall(
                match.Groups["toolName"].Value.Trim(),
                BuildArgPairParameters(match.Groups["args"].Value),
                match.Value))
            .ToList();

        // 2. If no [TOOL_CALL] blocks found, try XML <invoke> format
        if (results.Count == 0)
        {
            results.AddRange(ParseXmlToolCallBlocks(response));
        }

        return results;
    }

    /// <summary>
    /// Extracts <c>--key "value"</c> argument pairs from a [TOOL_CALL] args section.
    /// Later entries with the same key overwrite earlier ones (preserving legacy dict semantics).
    /// </summary>
    private static Dictionary<string, object?> BuildArgPairParameters(string argsText)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var pairs = ArgPairRegex().Matches(argsText)
            .Select(argMatch =>
            {
                var key = argMatch.Groups["key"].Success ? argMatch.Groups["key"].Value : argMatch.Groups["key2"].Value;
                var value = argMatch.Groups["value"].Success ? argMatch.Groups["value"].Value : argMatch.Groups["value2"].Value;
                var normalizedKey = NormalizeParameterName(key);
                return (NormalizedKey: normalizedKey, Value: SanitizeParameterValue(value));
            });

        foreach (var pair in pairs)
        {
            parameters[pair.NormalizedKey] = pair.Value;
        }

        return parameters;
    }

    /// <summary>
    /// Parses XML-style tool call blocks (&lt;invoke name="tool_name"&gt;) from an LLM text response.
    /// Handles three parameter formats:
    /// - Inline attributes: &lt;invoke name="tool" path="." recursive="true"/&gt;
    /// - Parameter elements: &lt;invoke name="tool"&gt;&lt;parameter name="path"&gt;value&lt;/parameter&gt;&lt;/invoke&gt;
    /// - Mixed: both inline attributes and parameter children
    /// </summary>
    private static List<ParsedToolCall> ParseXmlToolCallBlocks(string response)
    {
        return XmlInvokeRegex().Matches(response)
            .Select(match => BuildXmlParsedToolCall(match))
            .Where(call => call is not null)
            .Select(call => call!)
            .ToList();
    }

    /// <summary>
    /// Builds a <see cref="ParsedToolCall"/> from an <c>&lt;invoke&gt;</c> regex match,
    /// combining inline attributes and <c>&lt;parameter&gt;</c> children. Returns <c>null</c>
    /// when the tool name is missing.
    /// </summary>
    private static ParsedToolCall? BuildXmlParsedToolCall(System.Text.RegularExpressions.Match match)
    {
        var toolName = match.Groups["toolName"].Value.Trim();
        if (string.IsNullOrEmpty(toolName))
            return null;

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        AppendInlineXmlAttributes(parameters, match.Groups["attrs"].Value);
        AppendXmlParameterChildren(parameters, match.Groups["body"].Value);

        return new ParsedToolCall(toolName, parameters, match.Value);
    }

    /// <summary>
    /// Adds inline XML attributes (skipping the "name" attribute) to the parameter dictionary.
    /// </summary>
    private static void AppendInlineXmlAttributes(Dictionary<string, object?> parameters, string attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs))
            return;

        var entries = XmlInlineAttrRegex().Matches(attrs)
            .Where(attrMatch => !string.Equals(attrMatch.Groups["key"].Value, "name", StringComparison.OrdinalIgnoreCase))
            .Select(attrMatch =>
            {
                var normalizedKey = NormalizeParameterName(attrMatch.Groups["key"].Value);
                return (NormalizedKey: normalizedKey, Value: SanitizeParameterValue(attrMatch.Groups["value"].Value));
            });

        foreach (var entry in entries)
        {
            parameters[entry.NormalizedKey] = entry.Value;
        }
    }

    /// <summary>
    /// Adds <c>&lt;parameter name="key"&gt;value&lt;/parameter&gt;</c> children to the parameter dictionary.
    /// </summary>
    private static void AppendXmlParameterChildren(Dictionary<string, object?> parameters, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;

        var entries = XmlParameterRegex().Matches(body)
            .Select(paramMatch =>
            {
                var normalizedKey = NormalizeParameterName(paramMatch.Groups["key"].Value.Trim());
                return (NormalizedKey: normalizedKey, Value: SanitizeParameterValue(paramMatch.Groups["value"].Value.Trim()));
            });

        foreach (var (NormalizedKey, Value) in entries)
        {
            parameters[NormalizedKey] = Value;
        }
    }

    /// <summary>
    /// Normalizes LLM-generated parameter names to match tool schema expectations.
    /// Handles common mismatches like css_selector → selector, etc.
    /// </summary>
    private static string NormalizeParameterName(string rawName)
    {
        // Remove leading dashes if any slipped through
        var name = rawName.TrimStart('-');

        // Known mappings for common LLM mismatches
#pragma warning disable CA1308 // normalized lowercase parameter name is the switch subject, not a comparison normalization
        return name.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "css_selector" => "selector",
            "xpath_selector" => "selector",
            "query" => "query",
            // LLMs frequently hallucinate parameter names for file tools
            "file_path" => "path",
            "filepath" => "path",
            "input_file" => "path",
            "input" => "path",
            "files" => "path",
            "file" => "path",
            // Common content/body variations for file_write
            "text" => "content",
            "body" => "content",
            "data" => "content",
            // Common directory_read variations
            "directory" => "path",
            "dir" => "path",
            "folder" => "path",
            "dir_path" => "path",
            "directory_path" => "path",
            // Common boolean variations
            "recurse" => "recursive",
            "include_subdirs" => "recursive",
            _ => name
        };
    }

    /// <summary>
    /// Sanitizes a parameter value to fix common LLM formatting issues.
    /// For example, LLMs sometimes wrap paths in JSON array brackets: ["path"] → path
    /// </summary>
    private static object SanitizeParameterValue(object value)
    {
        if (value is not string strValue || string.IsNullOrEmpty(strValue))
            return value;

        var trimmed = strValue.Trim().TrimEnd(',', ';', ':');

        // Fix JSON array-wrapped values: ["some/path"] → some/path
        if (TryUnwrapJsonArray(trimmed, out var unwrappedArray))
            return unwrappedArray;

        // Fix double-quoted paths: "\"some/path\"" → some/path
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length > 2)
            trimmed = trimmed[1..^1];

        // Unescape common LLM escape sequences: literal \n → newline, \t → tab, \\ → backslash
        trimmed = UnescapeLlmText(trimmed);

        return TryCoerceScalar(trimmed);
    }

    /// <summary>
    /// Attempts to unwrap a JSON array-wrapped value (e.g. <c>["some/path"]</c>) to its inner single element.
    /// Falls back to stripping the brackets when the content is not valid JSON.
    /// Returns <c>true</c> only when an unwrapping produced a non-empty replacement.
    /// </summary>
    private static bool TryUnwrapJsonArray(string trimmed, out string unwrapped)
    {
        unwrapped = string.Empty;
        if (!(trimmed.StartsWith('[') && trimmed.EndsWith(']')))
            return false;

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(trimmed);
            if (parsed is { Length: 1 })
            {
                unwrapped = parsed[0];
                return true;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not valid JSON array, strip brackets as last resort
            var inner = trimmed[1..^1].Trim().Trim('"');
            if (!string.IsNullOrEmpty(inner))
            {
                unwrapped = inner;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Coerces a trimmed string value to a <see cref="bool"/> or <see cref="int"/> when possible,
    /// otherwise returns the original string.
    /// </summary>
    private static object TryCoerceScalar(string trimmed)
    {
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;

        if (int.TryParse(trimmed, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var intValue))
            return intValue;

        return trimmed;
    }

    /// <summary>
    /// Unescapes common LLM text escape sequences.
    /// LLMs frequently emit literal <c>\n</c> (two characters: backslash + n) instead of actual newline characters.
    /// This method converts those literal sequences to their real character equivalents.
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
}
