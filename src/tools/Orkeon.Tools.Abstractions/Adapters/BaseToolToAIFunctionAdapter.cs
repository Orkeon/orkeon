using Orkeon.Domain.Tools;
using Microsoft.Extensions.AI;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Abstractions.Adapters;

/// <summary>
/// Converts IBaseTool instances to AIFunction for native function calling via IChatClient.
/// </summary>
public static class BaseToolToAIFunctionAdapter
{
    /// <summary>
    /// Converts a single IBaseTool to an AIFunction.
    /// </summary>
    public static AIFunction ToAIFunction(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var schema = tool.Schema;

        return AIFunctionFactory.Create(
            method: async (AIFunctionArguments arguments, CancellationToken ct) =>
            {
                var parameters = arguments
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var request = new ToolCallRequest(
                    ToolName: tool.Name,
                    Parameters: parameters);

                var response = await tool.CallAsync(request, ct).ConfigureAwait(false);

                if (response.Success)
                    return response.Result?.ToString() ?? string.Empty;

                return $"Error: {response.Error}";
            },
            name: tool.Name,
            description: schema.Description);
    }

    /// <summary>
    /// Converts all tools to a list of AITools for use in ChatOptions.Tools.
    /// </summary>
    public static IList<AITool> ToAITools(IEnumerable<IBaseTool> tools)
    {
        return tools
            .Select(t => (AITool)ToAIFunction(t))
            .ToList();
    }
}
