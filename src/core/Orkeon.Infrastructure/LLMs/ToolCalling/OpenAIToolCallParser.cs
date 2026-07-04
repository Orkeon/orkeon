using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Extracts tool calls from OpenAI-format JSON responses and formats
/// results for multi-turn conversation history.
/// </summary>
public sealed partial class OpenAIToolCallParser : IToolCallParser
{
    private readonly ILogger<OpenAIToolCallParser> _logger;

    /// <summary>Initializes a new instance of <see cref="OpenAIToolCallParser"/>.</summary>
    /// <param name="logger">The logger.</param>
    public OpenAIToolCallParser(ILogger<OpenAIToolCallParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ParsedToolCall> ParseToolCalls(JsonElement responseBody)
    {
        var message = responseBody
            .GetProperty("choices")[0]
            .GetProperty("message");

        if (!message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ParsedToolCall>();

        foreach (var tc in toolCalls.EnumerateArray())
        {
            var id = tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()!
                : $"call_{Guid.NewGuid():N}";

            var function = tc.GetProperty("function");
            var name = function.GetProperty("name").GetString()!;
            var argumentsRaw = function.GetProperty("arguments").GetString() ?? "{}";

            Dictionary<string, object?> arguments;
            try
            {
                using var argDoc = JsonDocument.Parse(argumentsRaw);
                arguments = ConvertToDict(argDoc.RootElement);
            }
            catch (JsonException ex)
            {
                LogInvalidToolCallJson(ex, name, id);
                arguments = new Dictionary<string, object?>();
            }

            results.Add(new ParsedToolCall(id, name, arguments));
        }

        return results;
    }

    /// <inheritdoc />
    public object FormatToolResult(ParsedToolCall toolCall, string result, bool success)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        return new Dictionary<string, object>
        {
            ["role"] = "tool",
            ["tool_call_id"] = toolCall.Id,
            ["content"] = success ? result : $"Error: {result}"
        };
    }

    /// <inheritdoc />
    public object FormatAssistantToolCallMessage(JsonElement responseBody)
    {
        var message = responseBody
            .GetProperty("choices")[0]
            .GetProperty("message");

        return JsonSerializer.Deserialize<Dictionary<string, object>>(message.GetRawText())!;
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> to a <see cref="Dictionary{String,Object}"/>.
    /// Delegates to the shared <see cref="Serialization.JsonElementConverter"/>.
    /// </summary>
    private static Dictionary<string, object?> ConvertToDict(JsonElement element)
        => Serialization.JsonElementConverter.ToDict(element);

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Invalid JSON in tool call arguments for {ToolName} (id={Id}). Using empty dict.")]
    private partial void LogInvalidToolCallJson(Exception ex, string toolName, string id);
}
