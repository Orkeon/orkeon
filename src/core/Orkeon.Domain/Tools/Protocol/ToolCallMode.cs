namespace Orkeon.Domain.Tools.Protocol;

/// <summary>
/// Controls how the LLM should handle tool calling.
/// </summary>
public enum ToolCallMode
{
    /// <summary>The LLM may call tools or respond with text.</summary>
    Auto,

    /// <summary>The LLM must call at least one tool.</summary>
    Required,

    /// <summary>The LLM must not call any tools.</summary>
    None
}
