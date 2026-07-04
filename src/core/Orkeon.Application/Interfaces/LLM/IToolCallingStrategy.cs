namespace Orkeon.Application.Interfaces.LLM;

/// <summary>
/// Combines a formatter and a parser for a given LLM provider,
/// providing a unified entry point for native tool calling support.
/// </summary>
public interface IToolCallingStrategy
{
    /// <summary>Gets the formatter that serializes tool schemas for this provider.</summary>
    IToolSchemaFormatter Formatter { get; }

    /// <summary>Gets the parser that extracts tool calls from this provider's responses.</summary>
    IToolCallParser Parser { get; }

    /// <summary>Indicates whether this provider supports native (structured) tool calling.</summary>
    bool SupportsNativeToolCalling { get; }
}
