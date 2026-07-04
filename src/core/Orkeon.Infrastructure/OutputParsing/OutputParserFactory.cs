using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.OutputParsing.Parsers;

namespace Orkeon.Infrastructure.OutputParsing;

/// <summary>
/// Factory for creating output parsers based on the desired format.
/// </summary>
public sealed class OutputParserFactory : IOutputParserFactory
{
    /// <inheritdoc />
    public IStructuredOutputParser CreateParser(OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => new JsonOutputParser(),
            OutputFormat.Yaml => new YamlOutputParser(),
            OutputFormat.Csv => new CsvOutputParser(),
            OutputFormat.Text or OutputFormat.KeyValue => new KeyValueOutputParser(),
            _ => throw new NotSupportedException($"Output format '{format}' is not supported for non-generic parsing")
        };
    }

    /// <inheritdoc />
    public IStructuredOutputParser<T> CreateParser<T>(OutputFormat format) where T : class, new()
    {
        return format switch
        {
            OutputFormat.Json => new JsonOutputParser<T>(),
            OutputFormat.Yaml => new YamlOutputParser<T>(),
            OutputFormat.Csv => new CsvOutputParser<T>(),
            OutputFormat.Text or OutputFormat.KeyValue => new KeyValueOutputParser<T>(),
            _ => throw new NotSupportedException($"Output format '{format}' is not supported for typed parsing to {typeof(T).Name}")
        };
    }
}
