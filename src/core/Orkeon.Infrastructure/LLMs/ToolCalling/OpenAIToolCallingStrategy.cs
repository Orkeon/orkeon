using Orkeon.Application.Interfaces.LLM;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.LLMs.ToolCalling;

/// <summary>
/// OpenAI-specific tool calling strategy that combines the OpenAI schema formatter
/// and tool call parser into a single entry point.
/// </summary>
public sealed class OpenAIToolCallingStrategy : IToolCallingStrategy
{
    /// <inheritdoc />
    public IToolSchemaFormatter Formatter { get; }

    /// <inheritdoc />
    public IToolCallParser Parser { get; }

    /// <inheritdoc />
    public bool SupportsNativeToolCalling => true;

    /// <summary>Initializes a new instance of <see cref="OpenAIToolCallingStrategy"/>.</summary>
    /// <param name="logger">The logger for the underlying <see cref="OpenAIToolCallParser"/>.</param>
    public OpenAIToolCallingStrategy(ILogger<OpenAIToolCallParser> logger)
    {
        Formatter = new OpenAIToolSchemaFormatter();
        Parser = new OpenAIToolCallParser(logger);
    }
}
