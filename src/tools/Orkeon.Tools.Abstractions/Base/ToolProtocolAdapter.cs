using System.Text.Json;
using Orkeon.Domain.Tools;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Handles protocol conversion between string input and the JSON protocol.
/// Extracted from ToolBase to follow Single Responsibility Principle.
/// </summary>
internal static class ToolProtocolAdapter
{
    /// <summary>
    /// Converts a string input to a ToolCallRequest.
    /// </summary>
    public static ProtocolToolCallRequest ConvertToRequest(string toolName, string input)
    {
        var parameters = new Dictionary<string, object?>();

        // Try to parse as JSON first
        if (!string.IsNullOrWhiteSpace(input) && input.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(input);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    parameters[prop.Name] = prop.Value.GetRawText();
                }
            }
            catch (JsonException)
            {
                // If JSON parsing fails, treat as single input parameter
                parameters["input"] = input;
            }
        }
        else
        {
            // Simple string input
            parameters["input"] = input;
        }

        return new ProtocolToolCallRequest(toolName, parameters);
    }

    /// <summary>
    /// Converts a ToolCallResponse to a ToolResult.
    /// </summary>
    public static ToolResult ConvertToToolResult(ProtocolToolCallResponse response)
    {
        if (response.Success)
        {
            var output = response.Result?.ToString() ?? string.Empty;
            return ToolResult.CreateSuccess(output);
        }
        else
        {
            return ToolResult.CreateError(response.Error ?? "Unknown error");
        }
    }
}
