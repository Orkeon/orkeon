using System.Text.Json;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.OutputParsing.Validation;

/// <summary>
/// Validates output against a JsonSchema if one is provided in the context.
/// Priority: 20.
/// </summary>
public sealed partial class SchemaValidator : IOutputValidator
{
    /// <inheritdoc />
    public string Name => "SchemaValidator";

    /// <inheritdoc />
    public int Priority => 20;

    /// <inheritdoc />
    public Task<OutputValidationResult> ValidateAsync(
        string output,
        OutputValidationContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        // Only validate if a schema is provided
        if (context.Schema == null)
        {
            return Task.FromResult(new OutputValidationResult(IsValid: true));
        }

        // Only applies to JSON format
        if (context.ExpectedFormat != OutputFormat.Json)
        {
            return Task.FromResult(new OutputValidationResult(IsValid: true));
        }

        try
        {
            var json = ExtractJsonContent(output);
            var isValid = context.Schema.Validate(json);

            if (!isValid)
            {
                return Task.FromResult(new OutputValidationResult(
                    IsValid: false,
                    ErrorMessage: "Output does not match the expected JSON schema.",
                    SuggestedFix: $"Please ensure your JSON output conforms to the schema: {context.Schema}"));
            }

            return Task.FromResult(new OutputValidationResult(IsValid: true));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new OutputValidationResult(
                IsValid: false,
                ErrorMessage: $"Cannot validate schema: {ex.Message}",
                SuggestedFix: "Please provide valid JSON output."));
        }
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
