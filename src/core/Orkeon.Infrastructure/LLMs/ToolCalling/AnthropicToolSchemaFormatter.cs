using Orkeon.Application.Interfaces.LLM;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Converts Orkeon <see cref="ToolSchema"/> definitions into the Anthropic Messages API
/// tool format (flat objects with <c>input_schema</c> instead of OpenAI's nested
/// <c>function.parameters</c> wrapper).
/// </summary>
internal sealed class AnthropicToolSchemaFormatter : IToolSchemaFormatter
{
    /// <inheritdoc />
    public Dictionary<string, object> FormatToolsForPayload(
        IReadOnlyList<ToolSchema> tools,
        ToolCallMode mode = ToolCallMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var payload = new Dictionary<string, object>();

        // The tools are sent in every mode: the Messages API refuses a history holding tool_use or
        // tool_result blocks when the request defines no tool, and a turn that allows no call (the
        // budget wrap-up, #218) follows such a history.
        var toolsList = new List<Dictionary<string, object>>();
        foreach (var schema in tools)
        {
            toolsList.Add(FormatSingleTool(schema));
        }

        payload["tools"] = toolsList;

        // Anthropic tool_choice mapping:
        //   Auto     -> {"type": "auto"}
        //   Required -> {"type": "any"}
        //   None     -> {"type": "none"}: the tools stay defined, no tool may be called
        payload["tool_choice"] = new Dictionary<string, object>
        {
            ["type"] = mode switch
            {
                ToolCallMode.Required => "any",
                ToolCallMode.None => "none",
                _ => "auto",
            },
        };

        return payload;
    }

    /// <summary>
    /// Formats a single <see cref="ToolSchema"/> into the Anthropic tool object format:
    /// <c>{ name, description, input_schema: { type: "object", properties, required } }</c>.
    /// </summary>
    private static Dictionary<string, object> FormatSingleTool(ToolSchema schema)
    {
        // The one JSON Schema writer of the framework (ParameterSchema.ToJsonSchema).
        var inputSchema = schema.ToInputJsonSchema();

        return new Dictionary<string, object>
        {
            ["name"] = schema.Name,
            ["description"] = schema.Description,
            ["input_schema"] = inputSchema
        };
    }
}
