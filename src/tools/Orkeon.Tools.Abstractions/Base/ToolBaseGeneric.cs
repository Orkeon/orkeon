using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Microsoft.Extensions.Logging;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ProtocolToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Generic typed base class for tools. Eliminates parameter parsing and result-building
/// boilerplate by automating the pipeline:
/// Dictionary → TRequest → ExecuteTypedAsync → TResponse → Dictionary.
/// Auto-generates Schema from [FieldSchema] attributes on TRequest if no explicit override.
/// </summary>
public abstract partial class ToolBase<TRequest, TResponse> : ToolBase
    where TRequest : class, new()
    where TResponse : class
{
    private readonly ComponentPipeline _pipeline = new();
    private ProtocolToolSchema? _generatedSchema;

    /// <summary>
    /// Initializes a new instance of <see cref="ToolBase{TRequest, TResponse}"/> with an optional logger.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    protected ToolBase(ILogger? logger = null) : base(logger) { }

    /// <summary>
    /// Schema auto-generated from [FieldSchema]/[ReturnSchema] attributes on TRequest/TResponse.
    /// Parameters inferred from TRequest, Returns inferred from TResponse.
    /// Override to provide a custom schema.
    /// </summary>
    public override ProtocolToolSchema Schema
    {
        get
        {
            if (_generatedSchema == null)
            {
                var paramSchema = ToolSchemaGenerator.GenerateSchema<TRequest>(Name, Description);
                var (returns, types) = ToolSchemaGenerator.GenerateReturnsWithTypes<TResponse>();
                _generatedSchema = paramSchema with
                {
                    Returns = returns.Count > 0 ? returns : null,
                    Types = types.Count > 0 ? types : null
                };
            }
            return _generatedSchema;
        }
    }

    /// <summary>
    /// Sealed override: handles deserialization, default injection, typed dispatch,
    /// and response conversion. Tools override ExecuteTypedAsync instead.
    /// </summary>
    protected sealed override Task<ProtocolToolCallResponse> ExecuteCoreAsync(
        ProtocolToolCallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreInternalAsync();

        async Task<ProtocolToolCallResponse> ExecuteCoreInternalAsync()
        {
            try
            {
                // Step 1: Inject YAML defaults for missing optional parameters
                var mergedParams = MergeWithYamlDefaults(request.Parameters);

                // Step 2: Deserialize merged parameters → TRequest
                var typedRequest = _pipeline.Deserialize(mergedParams);

                // Step 3: Optional custom validation hook
                var validationError = ValidateTypedRequest(typedRequest);
                if (validationError is not null)
                {
                    return new ProtocolToolCallResponse(Success: false, Result: null, Error: validationError);
                }

                // Step 4: Dispatch to typed business logic
                var typedResponse = await ExecuteTypedAsync(typedRequest, cancellationToken).ConfigureAwait(false);

                // Step 5: Convert TResponse → Dictionary for FilterOutput compatibility
                var resultDict = _pipeline.Serialize(typedResponse);

                // Step 6: Filter output to declared returns (if Schema defines returns)
                var filteredResult = FilterOutput(resultDict);

                // Propagate success/error from typed response if present.
                // ExtractErrorMessage handles both `error` (string) and `errors`
                // (list) — see ToolBase.ExtractErrorMessage for the rationale.
                var success = true;
                string? error = null;

                if (resultDict.TryGetValue("success", out var successObj) && successObj is bool successBool)
                {
                    success = successBool;
                }

                if (!success)
                {
                    error = ExtractErrorMessage(resultDict);
                }

                return new ProtocolToolCallResponse(Success: success, Result: filteredResult, Error: error);
            }
            catch (JsonException ex)
            {
                LogParameterDeserializationFailed(ex, Name);
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: $"Invalid parameters: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Pure business logic. Implement this instead of ExecuteCoreAsync.
    /// Parameter parsing and result conversion are handled by the base class.
    /// </summary>
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Optional: override to add domain-level validation beyond schema-level checks.
    /// Return null if valid, or an error message string if invalid.
    /// </summary>
    protected virtual string? ValidateTypedRequest(TRequest request) => null;

    /// <summary>
    /// Merges YAML default values into the parameters dictionary for missing optional parameters.
    /// </summary>
    private Dictionary<string, object?> MergeWithYamlDefaults(Dictionary<string, object?> parameters)
    {
        var merged = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase);

        if (Schema?.Parameters == null) return merged;

        foreach (var (key, paramDef) in Schema.Parameters)
        {
            if (!paramDef.Required && paramDef.Default is not null && !merged.ContainsKey(key))
            {
                merged[key] = CoerceYamlDefault(paramDef.Default, paramDef.Type);
            }
        }

        return merged;
    }

    /// <summary>
    /// Converts a YAML default value to the proper .NET type based on the declared YAML type.
    /// </summary>
    private static object CoerceYamlDefault(object value, string yamlType)
    {
        // Format invariantly: a boxed double like 0.3 would otherwise stringify as "0,3" under a
        // comma-decimal culture, and the invariant TryParse below would then read it as 3.0
        // (comma treated as a group separator). Keep the round-trip culture-stable.
        var str = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        if (str is null) return value;

        return yamlType switch
        {
            "boolean" when bool.TryParse(str, out var b) => b,
            "integer" when int.TryParse(str, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
            "number" when double.TryParse(str, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => value
        };
    }

    /// <summary>
    /// Filters the output dictionary to only include keys declared in the returns schema.
    /// If no returns are declared, returns the full dictionary.
    /// </summary>
    private Dictionary<string, object?> FilterOutput(Dictionary<string, object?> output)
    {
        if (Schema?.Returns == null || Schema.Returns.Count == 0)
            return output;

        var filtered = new Dictionary<string, object?>();
        foreach (var (key, _) in Schema.Returns)
        {
            if (output.TryGetValue(key, out var value))
            {
                filtered[key] = value;
            }
        }
        return filtered;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Parameter deserialization failed for tool {ToolName}")]
    private partial void LogParameterDeserializationFailed(Exception ex, string toolName);

    /// <summary>
    /// Private composition helper that exposes ComponentBase pipeline methods.
    /// </summary>
    private sealed class ComponentPipeline : ComponentBase<TRequest, TResponse>
    {
        public TRequest Deserialize(Dictionary<string, object?> parameters)
            => DeserializeRequest(parameters);

        public Dictionary<string, object?> Serialize(TResponse response)
            => SerializeResponse(response);

        protected override Task<TResponse> ExecuteTypedAsync(TRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException("Pipeline helper does not execute.");
    }
}
