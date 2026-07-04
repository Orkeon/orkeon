using YamlDotNet.RepresentationModel;
using Orkeon.Domain.SharedKernel;
using System.Text.Json;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Base;

namespace Orkeon.Infrastructure.Evaluation.Evaluators;

/// <summary>
/// Typed input for format compliance evaluation.
/// </summary>
public sealed record FormatComplianceInput
{
    /// <summary>Gets the output text to evaluate.</summary>
    public string Output { get; init; } = string.Empty;
    /// <summary>Gets the expected output format name.</summary>
    public string Format { get; init; } = nameof(OutputFormat.Text);
}

/// <summary>
/// Typed result for format compliance evaluation.
/// </summary>
public sealed record FormatComplianceResult
{
    /// <summary>Gets a value indicating whether the output is valid.</summary>
    public bool IsValid { get; init; }
    /// <summary>Gets the reason for the validation result.</summary>
    public string Reason { get; init; } = string.Empty;
    /// <summary>Gets the format that was evaluated.</summary>
    public string Format { get; init; } = string.Empty;
}

/// <summary>
/// Checks whether the output matches the expected format (JSON, YAML, XML, CSV, Markdown).
/// Score: 1.0 if valid, 0.0 if not.
/// </summary>
public sealed class FormatComplianceEvaluator : EvaluatorBase<FormatComplianceInput, FormatComplianceResult>
{
    /// <inheritdoc />
    public override string Name => "FormatCompliance";
    /// <inheritdoc />
    public override string Description => "Checks if the output conforms to the expected format (JSON, YAML, XML, etc.).";

    /// <inheritdoc />
    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        var format = input.ExpectedFormat ?? OutputFormat.Text;
        return new Dictionary<string, object?>
        {
            ["output"] = input.Output,
            ["format"] = format.ToString()
        };
    }

    /// <inheritdoc />
    protected override Task<FormatComplianceResult> ExecuteTypedAsync(FormatComplianceInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var format = Enum.TryParse<OutputFormat>(request.Format, ignoreCase: true, out var parsed)
            ? parsed
            : OutputFormat.Text;

        var (isValid, reason) = ValidateFormat(request.Output, format);

        return Task.FromResult(new FormatComplianceResult
        {
            IsValid = isValid,
            Reason = reason,
            Format = format.ToString()
        });
    }

    /// <inheritdoc />
    protected override double ExtractScore(FormatComplianceResult result) => result.IsValid ? 1.0 : 0.0;

    /// <inheritdoc />
    protected override string? ExtractReasoning(FormatComplianceResult result) => result.Reason;

    private static (bool IsValid, string Reason) ValidateFormat(string output, OutputFormat format)
    {
        if (string.IsNullOrWhiteSpace(output))
            return (false, "Output is empty.");

        return format switch
        {
            OutputFormat.Json => ValidateJson(output),
            OutputFormat.Yaml => ValidateYaml(output),
            OutputFormat.Xml => ValidateXml(output),
            OutputFormat.Csv => ValidateCsv(output),
            OutputFormat.Markdown => ValidateMarkdown(output),
            OutputFormat.Text => (true, "Text format accepts any non-empty output."),
            OutputFormat.Custom => (true, "Custom format accepts any non-empty output."),
            _ => (true, "Unknown format; accepted by default.")
        };
    }

    private static (bool, string) ValidateJson(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            return (true, "Valid JSON.");
        }
        catch (JsonException ex)
        {
            return (false, $"Invalid JSON: {ex.Message}");
        }
    }

    private static (bool, string) ValidateYaml(string output)
    {
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(output));
            return (true, "Valid YAML.");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return (false, $"Invalid YAML: {ex.Message}");
        }
    }

    private static (bool, string) ValidateXml(string output)
    {
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(output);
            return (true, "Valid XML.");
        }
        catch (System.Xml.XmlException ex)
        {
            return (false, $"Invalid XML: {ex.Message}");
        }
    }

    private static (bool, string) ValidateCsv(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return (false, "CSV has no lines.");

        var headerColumns = lines[0].Split(',').Length;
        if (headerColumns < 1)
            return (false, "CSV header has no columns.");

        for (var i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',').Length;
            if (cols != headerColumns)
                return (false, $"CSV row {i + 1} has {cols} columns but header has {headerColumns}.");
        }

        return (true, "Valid CSV.");
    }

    private static (bool, string) ValidateMarkdown(string output)
    {
        // Markdown is free-form text; check for at least one markdown construct.
        var hasMarkdown = output.Contains('#', StringComparison.Ordinal) ||
                          output.Contains("**", StringComparison.Ordinal) ||
                          output.Contains("- ", StringComparison.Ordinal) ||
                          output.Contains("* ", StringComparison.Ordinal) ||
                          output.Contains("```", StringComparison.Ordinal) ||
                          output.Contains('[', StringComparison.Ordinal) ||
                          output.Contains('|', StringComparison.Ordinal);
        return hasMarkdown
            ? (true, "Contains markdown constructs.")
            : (false, "No recognizable markdown constructs found.");
    }
}
