using Orkeon.Application.Interfaces.LLM;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// Null-object strategy for providers that do not support native tool calling.
/// Accessing <see cref="Formatter"/> or <see cref="Parser"/> throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class NullToolCallingStrategy : IToolCallingStrategy
{
    /// <inheritdoc />
    public IToolSchemaFormatter Formatter =>
        throw new NotSupportedException("NullToolCallingStrategy does not support tool formatting");

    /// <inheritdoc />
    public IToolCallParser Parser =>
        throw new NotSupportedException("NullToolCallingStrategy does not support tool call parsing");

    /// <inheritdoc />
    public bool SupportsNativeToolCalling => false;
}
