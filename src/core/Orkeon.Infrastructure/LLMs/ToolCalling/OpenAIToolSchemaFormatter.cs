using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Converts Orkeon <see cref="ToolSchema"/> definitions into the OpenAI
/// function-calling JSON format (tools array + tool_choice).
/// </summary>
internal sealed class OpenAIToolSchemaFormatter : IToolSchemaFormatter
{
    /// <inheritdoc />
    public Dictionary<string, object> FormatToolsForPayload(
        IReadOnlyList<ToolSchema> tools,
        ToolCallMode mode = ToolCallMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var toolsList = new List<Dictionary<string, object>>(tools.Count);

        foreach (var schema in tools)
        {
            toolsList.Add(FormatSingleTool(schema));
        }

        var toolChoice = mode switch
        {
            ToolCallMode.Auto => "auto",
            ToolCallMode.Required => "required",
            ToolCallMode.None => "none",
            _ => "auto"
        };

        return new Dictionary<string, object>
        {
            ["tools"] = toolsList,
            ["tool_choice"] = toolChoice
        };
    }

    private static Dictionary<string, object> FormatSingleTool(ToolSchema schema)
    {
        // The one JSON Schema writer of the framework (ParameterSchema.ToJsonSchema); `required` is always
        // present, empty for none, as this format has always carried it.
        var parametersObject = schema.ToInputJsonSchema();
        parametersObject.TryAdd("required", new List<string>());

        var description = string.IsNullOrEmpty(schema.Description)
            ? schema.Name
            : schema.Description;

        var function = new Dictionary<string, object>
        {
            ["name"] = schema.Name,
            ["description"] = description,
            ["parameters"] = parametersObject
        };

        return new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = function
        };
    }
}
