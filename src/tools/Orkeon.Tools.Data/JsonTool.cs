using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Tools.Data;

// ── Enums ─────────────────────────────────────────────────────────────────

/// <summary>
/// JSON operations supported by the JsonTool.
/// </summary>
public enum JsonOperation
{
    /// <summary>Parse the JSON and return structure metadata.</summary>
    Parse,

    /// <summary>Query a value using a dot-notation path expression.</summary>
    Query,

    /// <summary>Format (pretty-print) the JSON string.</summary>
    Format
}

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for JsonTool.
/// </summary>
public sealed class JsonToolRequest
{
    /// <summary>Gets or sets the JSON string to process.</summary>
    [JsonPropertyName("input")]
    [FieldSchema(Description = "The JSON string to process", Example = "{\"name\": \"Alice\", \"age\": 30}")]
    public string Input { get; set; } = string.Empty;

    /// <summary>Gets or sets the operation to perform: Parse, Query, or Format.</summary>
    [JsonPropertyName("operation")]
    [FieldSchema(Description = "The operation to perform: parse, query, or format", Example = "Parse")]
    public JsonOperation Operation { get; set; }

    /// <summary>Gets or sets the dot-notation path for the Query operation.</summary>
    [JsonPropertyName("query")]
    [FieldSchema(Description = "Dot-notation path for query operation (e.g., 'data.items[0].name')", Example = "name")]
    public string? Query { get; set; }

    /// <summary>Initializes a new instance of <see cref="JsonToolRequest"/>.</summary>
    // Parameterless ctor required by ComponentBase deserialization
    public JsonToolRequest() { }
}

/// <summary>
/// Strongly-typed response for JsonTool.
/// </summary>
public sealed class JsonToolResponse
{
    /// <summary>Gets or sets whether the JSON was successfully parsed.</summary>
    [JsonPropertyName("parsed")]
    [ReturnSchema(Description = "Whether the JSON was successfully parsed", Example = true)]
    public bool? Parsed { get; set; }

    /// <summary>Gets or sets parse metadata such as type, validity, and property names.</summary>
    [JsonPropertyName("info")]
    [ReturnSchema(Description = "Parse metadata: type, validity, property count/names")]
    public Dictionary<string, object>? Info { get; init; }

    /// <summary>Gets or sets the result value from a parse or query operation.</summary>
    [JsonPropertyName("value")]
    [ReturnSchema(Description = "Result value: raw text for parse, extracted value for query", Example = "Alice")]
    public object? Value { get; set; }

    /// <summary>Gets or sets the dot-notation query that was executed.</summary>
    [JsonPropertyName("query")]
    [ReturnSchema(Description = "The dot-notation query that was executed", Example = "data.items[0].name")]
    public string? Query { get; set; }

    /// <summary>Gets or sets the JSON value type of the query result.</summary>
    [JsonPropertyName("type")]
    [ReturnSchema(Description = "JSON value type of the query result (String, Number, Object, etc.)", Example = "String")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the formatted (pretty-printed) JSON string.</summary>
    [JsonPropertyName("formatted")]
    [ReturnSchema(Description = "The formatted (pretty-printed) JSON string", Example = "{\n  \"name\": \"Alice\",\n  \"age\": 30\n}")]
    public string? Formatted { get; set; }

    /// <summary>Gets or sets the length of the formatted JSON string.</summary>
    [JsonPropertyName("length")]
    [ReturnSchema(Description = "Length of the formatted JSON string", Example = 42)]
    public int? Length { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for JSON manipulation: parse, query (JSONPath-like), and format.
/// Uses System.Text.Json for native JSON processing.
/// </summary>
[ToolContract("json_tool",
    Name = "json_tool",
    Description = "JSON manipulation tool supporting parse, query, and format operations.",
    Category = "Data Operations")]
public partial class JsonTool : ToolBase<JsonToolRequest, JsonToolResponse>
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true, MaxDepth = SerializationDefaults.JsonMaxDepth };

    private readonly IFileSystemService _fileSystemService;

    /// <summary>Initializes a new instance of <see cref="JsonTool"/> with virtual file system support.</summary>
    /// <param name="fileSystemService">
    /// Virtual file system service. An <c>input</c> that is a virtual path
    /// (leading <c>/</c>) is read from the VFS instead of being parsed as a literal JSON string.
    /// </param>
    /// <param name="logger">Optional logger instance.</param>
    public JsonTool(IFileSystemService fileSystemService, ILogger<JsonTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(JsonToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Input))
            return "Input cannot be empty";

        return null;
    }

    /// <inheritdoc />
    protected override Task<JsonToolResponse> ExecuteTypedAsync(JsonToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<JsonToolResponse> ExecuteTypedCoreAsync()
        {
            var json = await ResolveJsonInputAsync(request.Input, cancellationToken).ConfigureAwait(false);
            try
            {
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Invalid JSON input: {ex.Message}", ex);
                }

                using (doc)
                {
                    return request.Operation switch
                    {
                        JsonOperation.Parse => HandleParse(doc),
                        JsonOperation.Query => HandleQuery(doc, request.Query),
                        JsonOperation.Format => HandleFormat(doc),
                        _ => throw new InvalidOperationException($"Unknown operation: {request.Operation}")
                    };
                }
            }
            catch (JsonException ex)
            {
                LogUnexpectedJsonError(ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Resolves the tool's <c>input</c> to a JSON string. When the input is a virtual path
    /// (leading <c>/</c> — a JSON document never starts with one), the file content is read
    /// from the VFS. Otherwise the input is treated as a literal JSON string.
    /// </summary>
    private async Task<string> ResolveJsonInputAsync(string input, CancellationToken ct)
    {
        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith('/'))
            return input;

        var content = await _fileSystemService.TryReadAllTextAsync(input.Trim(), ct).ConfigureAwait(false);
        if (content is null)
            throw new InvalidOperationException($"File not found: {input.Trim()}");

        return content;
    }

    private static JsonToolResponse HandleParse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var info = new Dictionary<string, object>
        {
            ["type"] = root.ValueKind.ToString(),
            ["valid"] = true
        };

        if (root.ValueKind == JsonValueKind.Object)
        {
            info["property_count"] = root.EnumerateObject().Count();
            info["properties"] = root.EnumerateObject().Select(p => p.Name).ToList();
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            info["element_count"] = root.GetArrayLength();
        }

        return new JsonToolResponse
        {
            Parsed = true,
            Info = info,
            Value = root.ToString() ?? ""
        };
    }

    private static JsonToolResponse HandleQuery(JsonDocument doc, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Query parameter is required for query operation");

        var current = doc.RootElement;

        try
        {
            var segments = ParseQueryPath(query);

            foreach (var segment in segments)
            {
                var (next, error) = NavigateSegment(current, segment);
                if (error != null)
                    throw new InvalidOperationException(error);
                current = next;
            }

            var resultValue = ExtractValue(current);

            return new JsonToolResponse
            {
                Query = query,
                Value = resultValue,
                Type = current.ValueKind.ToString()
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Query failed: {ex.Message}", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException($"Query failed: {ex.Message}", ex);
        }
    }

    private static (JsonElement Element, string? Error) NavigateSegment(JsonElement current, QuerySegment segment)
    {
        if (segment.IsArrayIndex)
            return NavigateArraySegment(current, segment);

        return NavigatePropertySegment(current, segment);
    }

    private static (JsonElement Element, string? Error) NavigateArraySegment(JsonElement current, QuerySegment segment)
    {
        if (current.ValueKind != JsonValueKind.Array)
            return (current, $"Cannot index into non-array at segment '{segment.Value}'");

        var index = segment.ArrayIndex;
        if (index < 0 || index >= current.GetArrayLength())
            return (current, $"Array index {index} out of range (length: {current.GetArrayLength()})");

        return (current[index], null);
    }

    private static (JsonElement Element, string? Error) NavigatePropertySegment(JsonElement current, QuerySegment segment)
    {
        if (current.ValueKind != JsonValueKind.Object)
            return (current, $"Cannot access property '{segment.Value}' on non-object");

        if (!current.TryGetProperty(segment.Value, out var next))
            return (current, $"Property '{segment.Value}' not found");

        return (next, null);
    }

    private static JsonToolResponse HandleFormat(JsonDocument doc)
    {
        var formatted = JsonSerializer.Serialize(doc.RootElement, s_indentedOptions);

        return new JsonToolResponse
        {
            Formatted = formatted,
            Length = formatted.Length
        };
    }

    private static List<QuerySegment> ParseQueryPath(string path)
    {
        var segments = new List<QuerySegment>();

        foreach (var part in path.Split('.'))
        {
            if (string.IsNullOrEmpty(part))
                continue;

            var bracketIndex = part.IndexOf('[', StringComparison.Ordinal);
            if (bracketIndex < 0)
            {
                segments.Add(new QuerySegment(part, false, 0));
                continue;
            }

            ParseArrayAccessSegments(segments, part, bracketIndex);
        }

        return segments;
    }

    private static void ParseArrayAccessSegments(List<QuerySegment> segments, string part, int bracketIndex)
    {
        var propName = part.Substring(0, bracketIndex);
        if (!string.IsNullOrEmpty(propName))
            segments.Add(new QuerySegment(propName, false, 0));

        var closeBracket = part.IndexOf(']', bracketIndex);
        if (closeBracket <= bracketIndex + 1)
            return;

        var indexStr = part.Substring(bracketIndex + 1, closeBracket - bracketIndex - 1);
        if (int.TryParse(indexStr, out var index))
            segments.Add(new QuerySegment(indexStr, true, index));
    }

    private static object ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private sealed record QuerySegment(string Value, bool IsArrayIndex, int ArrayIndex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in JSON tool")]
    private partial void LogUnexpectedJsonError(Exception ex);
}
