using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Converts Orkeon <see cref="ToolSchema"/> definitions into the OpenAI
/// function-calling JSON format (tools array + tool_choice).
/// </summary>
public sealed class OpenAIToolSchemaFormatter : IToolSchemaFormatter
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
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var (name, param) in schema.Parameters)
        {
            properties[name] = FormatParameter(param);

            if (param.Required)
                required.Add(name);
        }

        var parametersObject = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };

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

    private static Dictionary<string, object> FormatParameter(ParameterSchema param)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = param.Type,
            ["description"] = param.Description
        };

        if (param.Default is not null)
            prop["default"] = param.Default;

        // EnumMap keys take priority over flat Enum list
        if (param.EnumMap is { Count: > 0 })
        {
            prop["enum"] = param.EnumMap.Keys.ToList();
        }
        else if (param.Enum is { Count: > 0 })
        {
            prop["enum"] = param.Enum;
        }

        if (param.Format is not null)
            prop["format"] = param.Format;

        if (param.Type == "array" && param.ItemsType is not null)
        {
            var items = new Dictionary<string, object> { ["type"] = param.ItemsType };
            if (param.ItemsFormat is not null)
                items["format"] = param.ItemsFormat;
            prop["items"] = items;
        }

        return prop;
    }
}
