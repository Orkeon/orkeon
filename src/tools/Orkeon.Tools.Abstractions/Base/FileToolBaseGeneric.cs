using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Microsoft.Extensions.Logging;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Generic typed base class for file tools. Combines FileToolBase capabilities
/// (path validation, directory creation) with the typed request/response pipeline
/// from ComponentBase, eliminating manual Dictionary TryGetValue boilerplate.
/// </summary>
public abstract partial class FileToolBase<TRequest, TResponse> : FileToolBase
    where TRequest : class, new()
    where TResponse : class
{
    private readonly ComponentPipeline _pipeline = new();
    private Domain.Tools.Protocol.ToolSchema? _generatedSchema;

    /// <summary>
    /// Schema auto-generated from [FieldSchema] attributes on TRequest.
    /// Override to provide a custom schema.
    /// </summary>
    public override Domain.Tools.Protocol.ToolSchema Schema
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
    /// Initializes a new instance of <see cref="FileToolBase{TRequest, TResponse}"/> with virtual file system support.
    /// </summary>
    /// <param name="fileSystemService">Virtual file system service for path resolution. Required.</param>
    /// <param name="logger">Optional logger.</param>
    protected FileToolBase(IFileSystemService fileSystemService, ILogger? logger = null)
        : base(fileSystemService, logger) { }

    /// <summary>
    /// Initializes a new instance of <see cref="FileToolBase{TRequest, TResponse}"/> with both VFS and path validation.
    /// </summary>
    /// <param name="fileSystemService">Virtual file system service for path resolution. Required.</param>
    /// <param name="pathValidator">Path validator for SSRF/traversal protection (defense in depth).</param>
    /// <param name="logger">Optional logger.</param>
    protected FileToolBase(IFileSystemService fileSystemService, IPathValidator pathValidator, ILogger? logger = null)
        : base(fileSystemService, pathValidator, logger) { }

    /// <summary>
    /// Sealed override: handles deserialization, default injection, typed dispatch,
    /// and response conversion. Tools override ExecuteTypedAsync instead.
    /// </summary>
    protected sealed override Task<ProtocolToolCallResponse> ExecuteCoreAsync(
        ProtocolToolCallRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreImplAsync();

        async Task<ProtocolToolCallResponse> ExecuteCoreImplAsync()
        {
            try
            {
                var mergedParams = MergeWithYamlDefaults(request.Parameters);
                var typedRequest = _pipeline.Deserialize(mergedParams);

                var validationError = ValidateTypedRequest(typedRequest);
                if (validationError is not null)
                {
                    return new ProtocolToolCallResponse(Success: false, Result: null, Error: validationError);
                }

                var typedResponse = await ExecuteTypedAsync(typedRequest, cancellationToken).ConfigureAwait(false);
                var resultDict = _pipeline.Serialize(typedResponse);
                var filteredResult = FilterOutput(resultDict);

                // Propagate success/error from typed response if present.
                // Recognise both `error` (singular string) and `errors` (plural list)
                // so typed responses like FileWriteResponse — which exposes a list
                // of citation validation violations under `errors` — surface a
                // meaningful message instead of an empty "tools.fileWrite failed: ".
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: ex.Message);
            }
        }
    }

    /// <summary>
    /// Pure business logic. Implement this instead of ExecuteCoreAsync.
    /// </summary>
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Optional: override to add domain-level validation beyond schema-level checks.
    /// Return null if valid, or an error message string if invalid.
    /// </summary>
    protected virtual string? ValidateTypedRequest(TRequest request) => null;

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

    private static object CoerceYamlDefault(object value, string yamlType)
    {
        var str = value.ToString();
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
