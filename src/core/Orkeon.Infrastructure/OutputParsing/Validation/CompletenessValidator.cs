using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Checks that all required fields are present in the output.
/// Works for JSON (checks properties), YAML (checks keys), and key-value (checks keys).
/// Priority: 30.
/// </summary>
public sealed partial class CompletenessValidator : IOutputValidator
{
    /// <inheritdoc />
    public string Name => "CompletenessValidator";

    /// <inheritdoc />
    public int Priority => 30;

    /// <inheritdoc />
    public Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        // Only validate if required fields are specified
        if (context.RequiredFields == null || context.RequiredFields.Count == 0)
        {
            return Task.FromResult(new OutputValidationResult(IsValid: true));
        }

        var missingFields = new List<string>();

        try
        {
            switch (context.ExpectedFormat)
            {
                case OutputFormat.Json:
                    missingFields = CheckJsonFields(output, context.RequiredFields);
                    break;
                case OutputFormat.Yaml:
                    missingFields = CheckTextFields(output, context.RequiredFields);
                    break;
                case OutputFormat.Text:
                case OutputFormat.KeyValue:
                    missingFields = CheckTextFields(output, context.RequiredFields);
                    break;
                default:
                    missingFields = CheckTextFields(output, context.RequiredFields);
                    break;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // If we can't parse, check for field names as text
            missingFields = CheckTextFields(output, context.RequiredFields);
        }
        catch (FormatException)
        {
            // If we can't parse, check for field names as text
            missingFields = CheckTextFields(output, context.RequiredFields);
        }

        if (missingFields.Count > 0)
        {
            return Task.FromResult(new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Missing required fields: {string.Join(", ", missingFields)}",
                SuggestedFix: $"Please include the following fields in your response: {string.Join(", ", missingFields)}"));
        }

        return Task.FromResult(new OutputValidationResult(IsValid: true));
    }

    private static List<string> CheckJsonFields(string output, IReadOnlyList<string> requiredFields)
    {
        var json = ExtractJsonContent(output);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return requiredFields.ToList();
        }

        var missing = requiredFields
            .Where(field => !root.EnumerateObject().Any(prop => string.Equals(prop.Name, field, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return missing;
    }

    private static List<string> CheckTextFields(string output, IReadOnlyList<string> requiredFields)
    {
        var missing = new List<string>();
        foreach (var field in requiredFields)
        {
            if (!output.Contains(field, StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(field);
            }
        }
        return missing;
    }

    private static string ExtractJsonContent(string text)
    {
        var trimmed = text.Trim();

        var match = JsonCodeBlockRegex().Match(text);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return trimmed;
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonCodeBlockRegex();
}
