using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Anthropic-specific tool calling strategy that combines the Anthropic schema formatter
/// and tool call parser into a single entry point.
/// </summary>
internal sealed class AnthropicToolCallingStrategy : IToolCallingStrategy
{
    /// <summary>
    /// The strategy an Anthropic provider built without one uses (#367): native tool calling, as
    /// <see cref="LlmProviderFactory"/> wires it. Stateless, so shared.
    /// </summary>
    internal static AnthropicToolCallingStrategy Default { get; } = new();

    /// <inheritdoc />
    public IToolSchemaFormatter Formatter { get; } = new AnthropicToolSchemaFormatter();

    /// <inheritdoc />
    public IToolCallParser Parser { get; } = new AnthropicToolCallParser();

    /// <inheritdoc />
    public bool SupportsNativeToolCalling => true;
}
