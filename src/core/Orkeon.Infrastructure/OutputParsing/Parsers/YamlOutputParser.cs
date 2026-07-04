using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Parsers;

/// <summary>
/// Shared YAML parsing configuration and helpers.
/// Avoids duplicating static fields across generic type instantiations (S2743).
/// </summary>
internal static partial class YamlOutputParserDefaults
{
    internal static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    internal static string ExtractYaml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Input text is empty");

        // Try extracting from markdown code blocks: ```yaml ... ``` or ```yml ... ```
        var codeBlockMatch = YamlCodeBlockRegex().Match(text);
        if (codeBlockMatch.Success)
        {
            return codeBlockMatch.Groups[1].Value.Trim();
        }

        // Otherwise use the raw text as YAML
        return text.Trim();
    }

    [GeneratedRegex(@"```(?:ya?ml)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex YamlCodeBlockRegex();
}

/// <summary>
/// Generic YAML output parser that deserializes LLM output to a typed object.
/// Handles raw YAML and YAML in markdown code blocks.
/// </summary>
public sealed class YamlOutputParser<T> : IStructuredOutputParser<T> where T : class
{
    /// <inheritdoc />
    public T Parse(string llmOutput)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);

        var yaml = YamlOutputParserDefaults.ExtractYaml(llmOutput);
        return YamlOutputParserDefaults.Deserializer.Deserialize<T>(yaml)
            ?? throw new InvalidOperationException($"YAML deserialization returned null for type {typeof(T).Name}");
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, out T? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            var yaml = YamlOutputParserDefaults.ExtractYaml(llmOutput);
            result = YamlOutputParserDefaults.Deserializer.Deserialize<T>(yaml);
            return result != null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<T> ParseAsync(string llmOutput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Parse(llmOutput));
    }
}

/// <summary>
/// Non-generic YAML output parser that uses runtime Type resolution.
/// </summary>
public sealed class YamlOutputParser : IStructuredOutputParser
{
    /// <inheritdoc />
    public object? Parse(string llmOutput, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);
        ArgumentNullException.ThrowIfNull(targetType);

        var yaml = YamlOutputParserDefaults.ExtractYaml(llmOutput);
        return YamlOutputParserDefaults.Deserializer.Deserialize(yaml, targetType);
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, Type targetType, out object? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            result = Parse(llmOutput, targetType);
            return result != null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return false;
        }
    }
}
