namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Parses LLM outputs into structured formats.
/// Supports JSON, typed objects, and custom formats.
/// </summary>
public interface IStructuredOutputParser<T> where T : class
{
    /// <summary>
    /// Parses LLM output into structured format.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output text</param>
    /// <returns>Parsed structured object</returns>
    T Parse(string llmOutput);

    /// <summary>
    /// Tries to parse LLM output, returning false if parsing fails.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output text</param>
    /// <param name="result">Parsed result if successful</param>
    /// <returns>True if parsing succeeded</returns>
    bool TryParse(string llmOutput, out T? result);

    /// <summary>
    /// Parses LLM output asynchronously (for complex parsing scenarios).
    /// </summary>
    /// <param name="llmOutput">Raw LLM output text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed structured object</returns>
    System.Threading.Tasks.Task<T> ParseAsync(string llmOutput, CancellationToken cancellationToken = default);
}

/// <summary>
/// Non-generic interface for runtime type resolution.
/// </summary>
public interface IStructuredOutputParser
{
    /// <summary>
    /// Parses LLM output into structured format.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output text</param>
    /// <param name="targetType">Target type to parse into</param>
    /// <returns>Parsed structured object</returns>
    object? Parse(string llmOutput, Type targetType);

    /// <summary>
    /// Tries to parse LLM output, returning false if parsing fails.
    /// </summary>
    /// <param name="llmOutput">Raw LLM output text</param>
    /// <param name="targetType">Target type to parse into</param>
    /// <param name="result">Parsed result if successful</param>
    /// <returns>True if parsing succeeded</returns>
    bool TryParse(string llmOutput, Type targetType, out object? result);
}

/// <summary>
/// Factory for creating output parsers.
/// </summary>
public interface IOutputParserFactory
{
    /// <summary>
    /// Creates a parser for the specified output format.
    /// </summary>
    /// <param name="format">Output format (json, yaml, csv, etc.)</param>
    /// <returns>Parser instance</returns>
    IStructuredOutputParser CreateParser(OutputFormat format);

    /// <summary>
    /// Creates a typed parser for the specified output format.
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    /// <param name="format">Output format</param>
    /// <returns>Typed parser instance</returns>
    IStructuredOutputParser<T> CreateParser<T>(OutputFormat format) where T : class, new();
}

/// <summary>
/// Supported output formats.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Plain text (no parsing).
    /// </summary>
    Text,

    /// <summary>
    /// JSON format.
    /// </summary>
    Json,

    /// <summary>
    /// YAML format.
    /// </summary>
    Yaml,

    /// <summary>
    /// CSV format.
    /// </summary>
    Csv,

    /// <summary>
    /// Key-value pairs.
    /// </summary>
    KeyValue,

    /// <summary>
    /// Markdown format.
    /// </summary>
    Markdown,

    /// <summary>
    /// Custom format.
    /// </summary>
    Custom
}
