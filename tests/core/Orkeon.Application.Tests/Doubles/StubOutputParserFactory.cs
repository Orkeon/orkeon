using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Doubles;

/// <summary><see cref="IOutputParserFactory"/> whose parser never parses anything.</summary>
internal sealed class StubOutputParserFactory : IOutputParserFactory
{
    public IStructuredOutputParser CreateParser(OutputFormat format) => new NoopParser();

    public IStructuredOutputParser<T> CreateParser<T>(OutputFormat format) where T : class, new() =>
        throw new NotSupportedException();

    private sealed class NoopParser : IStructuredOutputParser
    {
        public object? Parse(string llmOutput, Type targetType) => null;

        public bool TryParse(string llmOutput, Type targetType, out object? result)
        {
            result = null;
            return false;
        }
    }
}
