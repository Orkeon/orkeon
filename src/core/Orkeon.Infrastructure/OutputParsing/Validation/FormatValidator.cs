using YamlDotNet.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Validates that output matches the expected format (e.g., valid JSON, valid YAML).
/// Priority: 10 (runs early).
/// </summary>
public sealed partial class FormatValidator : IOutputValidator
{
    /// <inheritdoc />
    public string Name => "FormatValidator";

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = context.ExpectedFormat switch
        {
            OutputFormat.Json => ValidateJson(output),
            OutputFormat.Yaml => ValidateYaml(output),
            OutputFormat.Csv => ValidateCsv(output),
            OutputFormat.Text or OutputFormat.KeyValue => new OutputValidationResult(IsValid: true),
            _ => new OutputValidationResult(IsValid: true)
        };

        return Task.FromResult(result);
    }

    private static OutputValidationResult ValidateJson(string output)
    {
        try
        {
            // Try to extract JSON from code blocks or raw
            var json = ExtractJsonContent(output);
            using var doc = JsonDocument.Parse(json);
            return new OutputValidationResult(IsValid: true);
        }
        catch (JsonException ex)
        {
            return new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Invalid JSON format: {ex.Message}",
                SuggestedFix: "Please respond with valid JSON. Ensure all braces and brackets are properly matched and strings are properly quoted.");
        }
    }

    private static OutputValidationResult ValidateYaml(string output)
    {
        try
        {
            var yaml = ExtractYamlContent(output);
            var deserializer = new DeserializerBuilder().Build();
            deserializer.Deserialize(new System.IO.StringReader(yaml));
            return new OutputValidationResult(IsValid: true);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Invalid YAML format: {ex.Message}",
                SuggestedFix: "Please respond with valid YAML. Ensure proper indentation and syntax.");
        }
    }

    private static OutputValidationResult ValidateCsv(string output)
    {
        var csv = ExtractCsvContent(output);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return new OutputValidationResult(
                IsValid: false,
                ErrorMessage: "CSV output is empty.",
                SuggestedFix: "Please provide CSV output with at least a header row.");
        }

        // Check that all rows have the same number of columns as the header
        var headerColumnCount = CountCsvColumns(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            var columnCount = CountCsvColumns(lines[i]);
            if (columnCount != headerColumnCount)
            {
                return new OutputValidationResult(
                    IsValid: false,
                    ErrorMessage: $"CSV row {i + 1} has {columnCount} columns but header has {headerColumnCount} columns.",
                    SuggestedFix: "Please ensure all CSV rows have the same number of columns as the header.");
            }
        }

        return new OutputValidationResult(IsValid: true);
    }

    private static int CountCsvColumns(string row)
    {
        // Simple CSV column counting (handles quoted fields)
        var count = 1;
        var inQuotes = false;
        foreach (var c in row)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
                count++;
        }
        return count;
    }

    private static string ExtractJsonContent(string text)
    {
        var trimmed = text.Trim();

        // Try code block
        var match = JsonCodeBlockRegex().Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return trimmed;
    }

    private static string ExtractYamlContent(string text)
    {
        var match = YamlCodeBlockRegex().Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return text.Trim();
    }

    private static string ExtractCsvContent(string text)
    {
        var match = CsvCodeBlockRegex().Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return text.Trim();
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonCodeBlockRegex();

    [GeneratedRegex(@"```(?:ya?ml)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex YamlCodeBlockRegex();

    [GeneratedRegex(@"```(?:csv)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex CsvCodeBlockRegex();
}
