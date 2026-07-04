using System.Text.Json;
using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation.Base;

namespace Orkeon.Infrastructure.Evaluation.Evaluators;

/// <summary>
/// Typed input for schema compliance evaluation.
/// </summary>
public sealed record SchemaComplianceInput
{
    /// <summary>Gets the output text to evaluate against the schema.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Gets the JSON schema definition against which to validate the output.</summary>
    public string? SchemaJson { get; init; }
}

/// <summary>
/// Typed result for schema compliance evaluation.
/// </summary>
public sealed record SchemaComplianceResult
{
    /// <summary>Gets the number of fields that matched the expected schema.</summary>
    public int Matched { get; init; }

    /// <summary>Gets the total number of required schema fields.</summary>
    public int Total { get; init; }

    /// <summary>Gets the compliance score as a fraction (0.0 to 1.0).</summary>
    public double Score { get; init; }

    /// <summary>Gets the human-readable reasoning for the score.</summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>Gets a map of field names to match/mismatch details.</summary>
    public Dictionary<string, object> FieldDetails { get; init; } = [];
}

/// <summary>
/// Checks whether a JSON output matches an expected schema (expressed as a JSON object
/// whose keys are expected fields and whose values are expected JSON value kinds).
/// Score is the fraction of required fields that are present with the correct type.
/// </summary>
public sealed class SchemaComplianceEvaluator : EvaluatorBase<SchemaComplianceInput, SchemaComplianceResult>
{
    /// <inheritdoc />
    public override string Name => "SchemaCompliance";

    /// <inheritdoc />
    public override string Description => "Checks if JSON output matches an expected schema (required fields and types).";

    /// <summary>
    /// Metadata key for the expected schema JSON.
    /// Example: {"name":"String","age":"Number","tags":"Array"}
    /// Supported type names: String, Number, Boolean, Array, Object.
    /// </summary>
    public const string SchemaMetadataKey = "expected_schema";

    /// <inheritdoc />
    protected override Dictionary<string, object?> BuildParametersFromInput(EvaluationInput input)
    {
        var schemaJson = input.Metadata?.GetValueOrDefault(SchemaMetadataKey);
        return new Dictionary<string, object?>
        {
            ["output"] = input.Output,
            ["schema_json"] = schemaJson ?? string.Empty
        };
    }

    /// <inheritdoc />
    protected override Task<SchemaComplianceResult> ExecuteTypedAsync(SchemaComplianceInput request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SchemaJson))
        {
            return Task.FromResult(new SchemaComplianceResult
            {
                Matched = 0,
                Total = 0,
                Score = 1.0,
                Reasoning = "No schema provided; skipping check."
            });
        }

        JsonDocument outputDoc;
        try
        {
            outputDoc = JsonDocument.Parse(request.Output);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new SchemaComplianceResult
            {
                Score = 0.0,
                Reasoning = $"Output is not valid JSON: {ex.Message}"
            });
        }

        Dictionary<string, string> schema;
        try
        {
            schema = JsonSerializer.Deserialize<Dictionary<string, string>>(request.SchemaJson)
                     ?? [];
        }
        catch (JsonException ex)
        {
            outputDoc.Dispose();
            return Task.FromResult(new SchemaComplianceResult
            {
                Score = 0.0,
                Reasoning = $"Schema metadata is not valid JSON: {ex.Message}"
            });
        }

        if (schema.Count == 0)
        {
            outputDoc.Dispose();
            return Task.FromResult(new SchemaComplianceResult
            {
                Matched = 0,
                Total = 0,
                Score = 1.0,
                Reasoning = "Schema has no required fields."
            });
        }

        var root = outputDoc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            outputDoc.Dispose();
            return Task.FromResult(new SchemaComplianceResult
            {
                Total = schema.Count,
                Score = 0.0,
                Reasoning = "Output root is not a JSON object."
            });
        }

        var matched = 0;
        var fieldDetails = new Dictionary<string, object>();

        foreach (var (field, expectedType) in schema)
        {
            if (root.TryGetProperty(field, out var prop))
            {
                var actual = prop.ValueKind;
                var expected = MapValueKind(expectedType);
                if (expected == null || actual == expected)
                {
                    matched++;
                    fieldDetails[field] = "match";
                }
                else
                {
                    fieldDetails[field] = $"type_mismatch: expected {expectedType}, got {actual}";
                }
            }
            else
            {
                fieldDetails[field] = "missing";
            }
        }

        outputDoc.Dispose();

        var score = (double)matched / schema.Count;
        var reasoning = $"{matched}/{schema.Count} required fields present with correct types.";

        return Task.FromResult(new SchemaComplianceResult
        {
            Matched = matched,
            Total = schema.Count,
            Score = score,
            Reasoning = reasoning,
            FieldDetails = fieldDetails
        });
    }

    /// <inheritdoc />
    protected override double ExtractScore(SchemaComplianceResult result) => result.Score;

    /// <inheritdoc />
    protected override string? ExtractReasoning(SchemaComplianceResult result) => result.Reasoning;

    private static JsonValueKind? MapValueKind(string typeName)
    {
#pragma warning disable CA1308 // lowercase is the required switch-key form, not a comparison normalization
        return typeName.Trim().ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "string" => JsonValueKind.String,
            "number" => JsonValueKind.Number,
            "boolean" or "bool" => null, // True or False both count
            "array" => JsonValueKind.Array,
            "object" => JsonValueKind.Object,
            "null" => JsonValueKind.Null,
            _ => null // Unknown type; accept any
        };
    }
}
