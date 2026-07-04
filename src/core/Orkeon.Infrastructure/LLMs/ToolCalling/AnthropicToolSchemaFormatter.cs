using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Converts Orkeon <see cref="ToolSchema"/> definitions into the Anthropic Messages API
/// tool format (flat objects with <c>input_schema</c> instead of OpenAI's nested
/// <c>function.parameters</c> wrapper).
/// </summary>
public sealed class AnthropicToolSchemaFormatter : IToolSchemaFormatter
{
    /// <inheritdoc />
    public Dictionary<string, object> FormatToolsForPayload(
        IReadOnlyList<ToolSchema> tools,
        ToolCallMode mode = ToolCallMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var payload = new Dictionary<string, object>();

        // Build the tools array
        var toolsList = new List<Dictionary<string, object>>();

        if (mode != ToolCallMode.None)
        {
            foreach (var schema in tools)
            {
                toolsList.Add(FormatSingleTool(schema));
            }
        }

        payload["tools"] = toolsList;

        // Anthropic tool_choice mapping:
        //   Auto     → {"type": "auto"}
        //   Required → {"type": "any"}
        //   None     → omit tool_choice (tools list is already empty)
        switch (mode)
        {
            case ToolCallMode.Auto:
                payload["tool_choice"] = new Dictionary<string, object> { ["type"] = "auto" };
                break;
            case ToolCallMode.Required:
                payload["tool_choice"] = new Dictionary<string, object> { ["type"] = "any" };
                break;
            // None: no tool_choice key
        }

        return payload;
    }

    /// <summary>
    /// Formats a single <see cref="ToolSchema"/> into the Anthropic tool object format:
    /// <c>{ name, description, input_schema: { type: "object", properties, required } }</c>.
    /// </summary>
    private static Dictionary<string, object> FormatSingleTool(ToolSchema schema)
    {
        var properties = new Dictionary<string, object>();
        var requiredParams = new List<string>();

        foreach (var (paramName, paramSchema) in schema.Parameters)
        {
            properties[paramName] = FormatParameterProperty(paramSchema);

            if (paramSchema.Required)
                requiredParams.Add(paramName);
        }

        var inputSchema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (requiredParams.Count > 0)
            inputSchema["required"] = requiredParams;

        return new Dictionary<string, object>
        {
            ["name"] = schema.Name,
            ["description"] = schema.Description,
            ["input_schema"] = inputSchema
        };
    }

    /// <summary>
    /// Maps a <see cref="ParameterSchema"/> to a JSON Schema property object.
    /// </summary>
    private static Dictionary<string, object> FormatParameterProperty(ParameterSchema param)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = param.Type,
            ["description"] = param.Description
        };

        if (param.Default is not null)
            prop["default"] = param.Default;

        // EnumMap (C# enum name→value mapping) takes priority over flat Enum list
        if (param.EnumMap is { Count: > 0 })
            prop["enum"] = param.EnumMap.Keys.ToList();
        else if (param.Enum is { Count: > 0 })
            prop["enum"] = param.Enum;

        if (param.Format is not null)
            prop["format"] = param.Format;

        if (param.ItemsType is not null)
        {
            var items = new Dictionary<string, object> { ["type"] = param.ItemsType };
            if (param.ItemsFormat is not null)
                items["format"] = param.ItemsFormat;
            prop["items"] = items;
        }

        return prop;
    }
}
