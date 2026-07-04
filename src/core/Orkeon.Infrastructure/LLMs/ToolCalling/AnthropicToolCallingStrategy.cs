using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Anthropic-specific tool calling strategy that combines the Anthropic schema formatter
/// and tool call parser into a single entry point.
/// </summary>
public sealed class AnthropicToolCallingStrategy : IToolCallingStrategy
{
    /// <inheritdoc />
    public IToolSchemaFormatter Formatter { get; } = new AnthropicToolSchemaFormatter();

    /// <inheritdoc />
    public IToolCallParser Parser { get; } = new AnthropicToolCallParser();

    /// <inheritdoc />
    public bool SupportsNativeToolCalling => true;
}
