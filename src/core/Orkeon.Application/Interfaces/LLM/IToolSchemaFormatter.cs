using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Interfaces.LLM;

/// <summary>
/// Converts Orkeon <see cref="ToolSchema"/> definitions into a provider-specific
/// payload fragment ready to be merged into the LLM request.
/// </summary>
public interface IToolSchemaFormatter
{
    /// <summary>
    /// Formats tool schemas for the LLM request payload.
    /// Returns entries to merge (e.g. "tools", "tool_choice").
    /// </summary>
    Dictionary<string, object> FormatToolsForPayload(
        IReadOnlyList<ToolSchema> tools,
        ToolCallMode mode = ToolCallMode.Auto);
}
