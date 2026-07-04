using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.OutputParsing.Parsers;

/// <summary>
/// Shared JSON parsing configuration and helpers.
/// Avoids duplicating static fields across generic type instantiations (S2743).
/// </summary>
internal static partial class JsonOutputParserDefaults
{
    internal static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    internal static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new JsonException("Input text is empty");

        // Try raw JSON first
        var trimmed = text.Trim();
        if (IsWrappedJson(trimmed))
            return trimmed;

        // Try extracting from markdown code blocks: ```json ... ``` or ``` ... ```
        var codeBlockMatch = JsonCodeBlockRegex().Match(text);
        if (codeBlockMatch.Success)
        {
            var content = codeBlockMatch.Groups[1].Value.Trim();
            if (IsWrappedJson(content))
                return content;
        }

        // Try to find JSON object or array in text using brace/bracket matching
        return ExtractJsonByBraceMatching(text, '{', '}')
            ?? ExtractJsonByBraceMatching(text, '[', ']')
            ?? throw new JsonException("No valid JSON found in the output");
    }

    /// <summary>
    /// Checks whether a string looks like a complete JSON object or array.
    /// </summary>
    private static bool IsWrappedJson(string text)
    {
        return (text.StartsWith('{') && text.EndsWith('}'))
            || (text.StartsWith('[') && text.EndsWith(']'));
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonCodeBlockRegex();

    internal static string? ExtractJsonByBraceMatching(string text, char openBrace, char closeBrace)
    {
        var startIdx = text.IndexOf(openBrace, StringComparison.Ordinal);
        if (startIdx < 0)
            return null;

        var depth = 0;
        var inString = false;

        for (int i = startIdx; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\')
                    i++; // Skip escaped character
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case var _ when c == openBrace:
                    depth++;
                    break;
                case var _ when c == closeBrace:
                    depth--;
                    if (depth == 0)
                        return text[startIdx..(i + 1)];
                    break;
            }
        }

        return null;
    }
}

/// <summary>
/// Generic JSON output parser that deserializes LLM output to a typed object.
/// Handles raw JSON, JSON in markdown code blocks, and JSON mixed with text.
/// </summary>
public sealed class JsonOutputParser<T> : IStructuredOutputParser<T> where T : class
{
    /// <inheritdoc />
    public T Parse(string llmOutput)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);

        var json = JsonOutputParserDefaults.ExtractJson(llmOutput);
        return JsonSerializer.Deserialize<T>(json, JsonOutputParserDefaults.DefaultOptions)
            ?? throw new JsonException($"Deserialization returned null for type {typeof(T).Name}");
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, out T? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            var json = JsonOutputParserDefaults.ExtractJson(llmOutput);
            result = JsonSerializer.Deserialize<T>(json, JsonOutputParserDefaults.DefaultOptions);
            return result != null;
        }
        catch (JsonException)
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
/// Non-generic JSON output parser that uses runtime Type resolution.
/// </summary>
public sealed class JsonOutputParser : IStructuredOutputParser
{
    /// <inheritdoc />
    public object? Parse(string llmOutput, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);
        ArgumentNullException.ThrowIfNull(targetType);

        var json = JsonOutputParserDefaults.ExtractJson(llmOutput);
        return JsonSerializer.Deserialize(json, targetType, JsonOutputParserDefaults.DefaultOptions);
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
        catch (JsonException)
        {
            return false;
        }
    }
}
