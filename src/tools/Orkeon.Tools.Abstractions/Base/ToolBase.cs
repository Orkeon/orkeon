using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Security;
using Microsoft.Extensions.Logging;
// Resolve ambiguity with explicit aliases
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ProtocolToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Serialization;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Unified base class for tool implementations.
/// Provides common functionality for validation, execution, and error handling.
/// Reads [ToolContract] attribute for default Name, Description, Category.
/// </summary>
public abstract partial class ToolBase : ITool
{
    private static readonly ConcurrentDictionary<Type, ToolContractAttribute?> s_contractCache = new();

    /// <summary>Logger instance for tool operations.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Established protected base-class field referenced directly by hundreds of derived tools across the framework; converting to a property would break the inherited contract without behavioral benefit.")]
    protected readonly ILogger _logger;

    /// <summary>JSON serializer options used for parameter and result serialization.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Established protected base-class field referenced directly by derived tools across multiple projects; converting to a property would break the inherited contract without behavioral benefit.")]
    protected readonly JsonSerializerOptions _jsonOptions;
    // ToolParameterValidator and ToolProtocolAdapter methods are now static — no instances needed.

    private readonly ToolContractAttribute? _contract;

    /// <summary>
    /// Gets the tool name. Defaults to [ToolContract].UniqueName (snake_case machine identifier)
    /// then falls back to [ToolContract].Name. This value is exposed as <c>function.name</c> to
    /// OpenAI-compatible providers, which enforce <c>^[a-zA-Z0-9_-]+$</c>; using UniqueName first
    /// keeps the human-readable <c>Name</c> available for UI/description purposes without leaking
    /// invalid characters (spaces, accents, punctuation) into the LLM payload.
    /// Override to customize.
    /// </summary>
    public virtual string Name => _contract?.UniqueName ?? _contract?.Name ?? GetType().Name;

    /// <summary>
    /// Gets the tool description. Defaults to [ToolContract].Description.
    /// Override to customize.
    /// </summary>
    public virtual string Description => _contract?.Description ?? "";

    /// <summary>
    /// Gets the tool parameters schema.
    /// </summary>
    public virtual ITypedToolParameters? Parameters => null;

    /// <summary>
    /// Gets whether the tool requires human approval.
    /// </summary>
    public virtual bool RequiresHumanApproval => false;

    /// <summary>
    /// Gets the tool category. Defaults to [ToolContract].Category or "General".
    /// Override to customize.
    /// </summary>
    public virtual string Category => _contract?.Category ?? "General";

    /// <summary>
    /// Gets the tool's rate limit (calls per minute).
    /// </summary>
    public virtual int? RateLimitPerMinute => null;

    /// <summary>
    /// Gets the JSON schema defining the tool's parameters and return type.
    /// Override to provide a custom schema. ToolBase&lt;TReq,TRes&gt; auto-generates from attributes.
    /// </summary>
    public virtual ProtocolToolSchema Schema => new(Name, Description, []);

    /// <summary>
    /// Initializes a new instance of <see cref="ToolBase"/> with an optional logger.
    /// </summary>
    /// <param name="logger">Optional logger. Defaults to <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger"/>.</param>
    protected ToolBase(ILogger? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            MaxDepth = SerializationDefaults.JsonMaxDepth,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        _contract = s_contractCache.GetOrAdd(GetType(),
            t => t.GetCustomAttributes(typeof(ToolContractAttribute), false)
                  .OfType<ToolContractAttribute>()
                  .FirstOrDefault());
    }

    /// <summary>
    /// Executes the tool with the given request using the new JSON protocol.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any tool execution failure is converted to a failed ProtocolToolCallResponse so one tool call cannot crash the agent loop; cancellation is preserved by the separate OperationCanceledException catch.")]
    public Task<ProtocolToolCallResponse> CallAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ProtocolToolCallResponse> CallCoreAsync()
        {
            try
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var serializedRequest = JsonSerializer.Serialize(request, _jsonOptions);
                    LogToolCalling(Name, serializedRequest);
                }

                // Validate parameters against schema
                var validationResult = ValidateParameters(request.Parameters);
                if (!validationResult.IsValid)
                {
                    return new ProtocolToolCallResponse(
                        Success: false,
                        Result: null,
                        Error: validationResult.Error ?? "Invalid parameters",
                        Metadata: new Dictionary<string, object?> { ["validation_error"] = true }
                    );
                }

                // Execute the tool with the new protocol
                var result = await ExecuteCoreAsync(request, cancellationToken).ConfigureAwait(false);

                LogToolCallCompleted(Name, result.Success);

                return result;
            }
            catch (OperationCanceledException)
            {
                LogToolCallCancelled(Name);
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: "Operation cancelled",
                    Metadata: new Dictionary<string, object?> { ["cancelled"] = true }
                );
            }
            catch (Exception ex)
            {
                LogToolCallError(ex, Name);
                return new ProtocolToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: $"Tool execution failed: {ex.Message}",
                    Metadata: new Dictionary<string, object?> { ["exception_type"] = ex.GetType().Name }
                );
            }
        }
    }

    /// <summary>
    /// Executes the tool with the given input (legacy method that converts to new protocol).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any failure in the legacy input adaptation or tool execution is converted to an error ToolResult so one tool call cannot crash the agent loop.")]
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            LogToolLegacyExecuting(Name, input);

            var request = ToolProtocolAdapter.ConvertToRequest(Name, input);
            var response = await CallAsync(request, cancellationToken).ConfigureAwait(false);

            return ToolProtocolAdapter.ConvertToToolResult(response);
        }
        catch (Exception ex)
        {
            LogToolLegacyExecutionError(ex, Name);
            return ToolResult.CreateError($"Tool execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Core execution logic to be implemented by derived classes using the new protocol.
    /// </summary>
    protected abstract Task<ProtocolToolCallResponse> ExecuteCoreAsync(ProtocolToolCallRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Legacy core execution logic - default implementation converts to new protocol.
    /// </summary>
    protected virtual async Task<ToolResult> ExecuteCoreAsync(string input, CancellationToken cancellationToken)
    {
        // Convert to new protocol and execute
        var parameters = new Dictionary<string, object?> { ["input"] = input };
        var request = new ProtocolToolCallRequest(Name, parameters);
        var response = await ExecuteCoreAsync(request, cancellationToken).ConfigureAwait(false);

        return ToolProtocolAdapter.ConvertToToolResult(response);
    }

    /// <summary>
    /// Validates parameters against the tool's schema.
    /// </summary>
    protected virtual ValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        return ToolParameterValidator.ValidateParameters(parameters, Schema);
    }

    /// <summary>
    /// Checks if a value matches the expected type.
    /// </summary>
    protected static bool IsValidType(object value, string expectedType)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        return ToolParameterValidator.IsValidType(value, expectedType);
    }

    /// <summary>
    /// Validates the input against the tool's parameter schema.
    /// </summary>
    public virtual bool ValidateInput(string input)
    {
        var result = ValidateInputInternal(input);
        return result.IsValid;
    }

    /// <summary>
    /// Internal validation method that returns detailed validation result.
    /// </summary>
    protected virtual ValidationResult ValidateInputInternal(string input)
    {
        if (Parameters == null)
            return ValidationResult.Success();

        return ToolParameterValidator.ValidateInput(input);
    }

    /// <summary>
    /// Parses JSON input to a strongly typed object.
    /// </summary>
    protected T? ParseJsonInput<T>(string input) where T : class
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(input, _jsonOptions);
        }
        catch (JsonException ex)
        {
            LogJsonInputParseFailed(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Creates a JSON response from an object.
    /// </summary>
    protected string CreateJsonResponse<T>(T response) where T : class
    {
        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    /// <summary>
    /// Extracts a meaningful error message from a serialized typed response.
    /// Recognises both <c>error</c> (singular string) and <c>errors</c> (plural
    /// list — e.g. <c>FileWriteResponse.Errors</c> citation violations). When
    /// neither exists, returns <see langword="null"/>; the caller is
    /// responsible for providing a fallback. Without this fallback path, a
    /// tool returning <c>Success=false</c> with a populated <c>errors</c>
    /// list would surface as an empty "tools.&lt;name&gt; failed:" to the
    /// script runner.
    /// </summary>
    protected static string? ExtractErrorMessage(Dictionary<string, object?> responseDict)
    {
        ArgumentNullException.ThrowIfNull(responseDict);
        if (responseDict.TryGetValue("error", out var singularObj) && singularObj is string singular && !string.IsNullOrWhiteSpace(singular))
            return singular;

        if (responseDict.TryGetValue("errors", out var pluralObj) && pluralObj is not null)
        {
            var joined = JoinErrorList(pluralObj);
            if (!string.IsNullOrWhiteSpace(joined))
                return joined;
        }

        return null;
    }

    private static string? JoinErrorList(object value)
    {
        if (value is string s) return s;
        if (value is System.Collections.IEnumerable enumerable)
        {
            var parts = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                var str = item.ToString();
                if (!string.IsNullOrWhiteSpace(str)) parts.Add(str!);
            }
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }
        return value.ToString();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Calling tool {ToolName} with request: {Request}")]
    private partial void LogToolCalling(string toolName, string request);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tool {ToolName} call completed. Success: {Success}")]
    private partial void LogToolCallCompleted(string toolName, bool success);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {ToolName} call was cancelled")]
    private partial void LogToolCallCancelled(string toolName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calling tool {ToolName}")]
    private partial void LogToolCallError(Exception ex, string toolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing tool {ToolName} with legacy input: {Input}")]
    private partial void LogToolLegacyExecuting(string toolName, string input);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing tool {ToolName} with legacy method")]
    private partial void LogToolLegacyExecutionError(Exception ex, string toolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse JSON input: {Error}")]
    private partial void LogJsonInputParseFailed(string error);
}
/// <summary>
/// IDisposable pattern for ToolBase.
/// </summary>
public abstract partial class ToolBase : IDisposable
{
    private bool _disposed;

    /// <summary>Releases all resources used by this tool instance.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release managed resources; <c>false</c> when called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            _disposed = true;
        }
    }
}
