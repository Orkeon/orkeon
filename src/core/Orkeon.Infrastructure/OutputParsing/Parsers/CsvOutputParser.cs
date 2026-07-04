using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Parsers;

/// <summary>
/// Shared CSV parsing configuration and helpers.
/// Avoids duplicating static fields across generic type instantiations (S2743).
/// </summary>
internal static partial class CsvOutputParserDefaults
{
    internal static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        Delimiter = ",",
        MissingFieldFound = null,
        HeaderValidated = null
    };

    internal static string ExtractCsv(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Input text is empty");

        // Try extracting from markdown code blocks: ```csv ... ```
        var codeBlockMatch = CsvCodeBlockRegex().Match(text);
        if (codeBlockMatch.Success)
        {
            return codeBlockMatch.Groups[1].Value.Trim();
        }

        // Otherwise use the raw text
        return text.Trim();
    }

    [GeneratedRegex(@"```(?:csv)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex CsvCodeBlockRegex();
}

/// <summary>
/// Generic CSV output parser that deserializes LLM output to a typed object.
/// For single-row CSV, returns the first record. For multi-row, wraps in a list.
/// </summary>
public sealed class CsvOutputParser<T> : IStructuredOutputParser<T> where T : class
{
    /// <inheritdoc />
    public T Parse(string llmOutput)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);

        var csv = CsvOutputParserDefaults.ExtractCsv(llmOutput);
        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, CsvOutputParserDefaults.Config);

        // If T is a list type, read all records
        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = typeof(T).GetGenericArguments()[0];
            var records = csvReader.GetRecords(elementType).ToList();
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
            foreach (var record in records)
            {
                list.Add(record);
            }
            return (T)(object)list;
        }

        // Single record: return first row
        var result = csvReader.GetRecords<T>().FirstOrDefault();
        return result ?? throw new InvalidOperationException($"No CSV records found for type {typeof(T).Name}");
    }

    /// <inheritdoc />
    public bool TryParse(string llmOutput, out T? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(llmOutput))
            return false;

        try
        {
            result = Parse(llmOutput);
            return result != null;
        }
        catch (Exception ex) when (ex is CsvHelper.CsvHelperException or FormatException or InvalidOperationException)
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
/// Non-generic CSV output parser that uses runtime Type resolution.
/// </summary>
public sealed class CsvOutputParser : IStructuredOutputParser
{
    /// <inheritdoc />
    public object? Parse(string llmOutput, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(llmOutput);
        ArgumentNullException.ThrowIfNull(targetType);

        var csv = CsvOutputParserDefaults.ExtractCsv(llmOutput);
        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, CsvOutputParserDefaults.Config);

        var records = csvReader.GetRecords(targetType).ToList();
        return records.FirstOrDefault();
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
        catch (Exception ex) when (ex is CsvHelper.CsvHelperException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }
}
