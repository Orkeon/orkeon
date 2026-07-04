using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using Orkeon.Trading.Tools.Domain.Models;
using Orkeon.Trading.Tools.Domain.Serialization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Trading.Tools.Infrastructure.Base;

/// <summary>
/// Base class for all trading tools providing common functionality.
/// Loads Name, Description, and Schema from YAML definitions in Domain/ToolDefinitions/.
/// Filters output to match declared returns schema.
/// </summary>
public abstract class TradingToolBase : Orkeon.Domain.Common.ITool
{
    protected readonly ILogger? _logger;
    protected readonly JsonSerializerOptions _jsonOptions;
    private readonly Lazy<ToolDefinition> _definition;

    /// <summary>
    /// Each tool declares its ID (= YAML filename without extension, e.g. "orderbook_depth").
    /// </summary>
    protected abstract string ToolId { get; }

    public string Name => _definition.Value.Name;
    public string Description => _definition.Value.Description;
    public ToolSchema Schema => _definition.Value.ToToolSchema();

    protected TradingToolBase(ILogger? logger = null)
    {
        _logger = logger;
        _definition = new Lazy<ToolDefinition>(() => ToolDefinitionLoader.Load(ToolId));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new DecimalInvariantConverter(),
                new DoubleInvariantConverter()
            }
        };
    }

    /// <summary>
    /// New protocol: structured tool calling with JSON request/response.
    /// Validates input parameters and filters output to match declared schema.
    /// </summary>
    public async Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Executing tool {ToolName}", Name);

            // Validate request
            if (!ValidateRequest(request, out var validationError))
            {
                return new ToolCallResponse(
                    Success: false,
                    Result: null,
                    Error: validationError
                );
            }

            // Execute core logic
            var result = await ExecuteCoreAsync(request, cancellationToken);

            // Filter output to match declared returns schema
            if (result.Success && result.Result is Dictionary<string, object> dict)
            {
                var filtered = FilterOutput(dict);
                result = result with { Result = filtered };
            }

            _logger?.LogInformation("Tool {ToolName} completed successfully", Name);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Tool {ToolName} was cancelled", Name);
            return new ToolCallResponse(
                Success: false,
                Result: null,
                Error: "Operation was cancelled"
            );
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error executing tool {ToolName}", Name);
            return new ToolCallResponse(
                Success: false,
                Result: null,
                Error: $"Unexpected error: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Legacy protocol: string-based execution (converts to/from JSON)
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = string.IsNullOrWhiteSpace(input)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(input, _jsonOptions)
                  ?? new Dictionary<string, object?>();

            var request = new ToolCallRequest(
                ToolName: Name,
                Parameters: parameters
            );

            var response = await CallAsync(request, cancellationToken);

            if (response.Success)
            {
                var resultJson = response.Result != null
                    ? JsonSerializer.Serialize(response.Result, _jsonOptions)
                    : string.Empty;

                return ToolResult.CreateSuccess(resultJson);
            }
            else
            {
                return ToolResult.CreateError(response.Error ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing tool {ToolName} with legacy protocol", Name);
            return ToolResult.CreateError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Legacy validation (checks if JSON is valid)
    /// </summary>
    public virtual bool ValidateInput(string input)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input))
                return true;

            JsonSerializer.Deserialize<Dictionary<string, object>>(input, _jsonOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates the tool call request against schema requirements
    /// </summary>
    protected virtual bool ValidateRequest(ToolCallRequest request, out string? error)
    {
        error = null;

        if (request.ToolName != Name)
        {
            error = $"Tool name mismatch: expected {Name}, got {request.ToolName}";
            return false;
        }

        foreach (var param in Schema.Parameters.Where(p => p.Value.Required))
        {
            if (!request.Parameters.ContainsKey(param.Key))
            {
                error = $"Required parameter '{param.Key}' is missing";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Core execution logic to be implemented by derived classes
    /// </summary>
    protected abstract Task<ToolCallResponse> ExecuteCoreAsync(
        ToolCallRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Helper to extract and validate a required parameter
    /// </summary>
    protected bool TryGetRequiredParameter<T>(
        Dictionary<string, object> parameters,
        string key,
        out T? value,
        out string? error)
    {
        error = null;
        value = default;

        if (!parameters.TryGetValue(key, out var objValue))
        {
            error = $"Required parameter '{key}' is missing";
            return false;
        }

        try
        {
            if (objValue is JsonElement jsonElement)
            {
                value = JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), _jsonOptions);
            }
            else if (objValue is T typedValue)
            {
                value = typedValue;
            }
            else if (TryConvertValue<T>(objValue, out var converted))
            {
                value = converted;
            }
            else
            {
                var json = JsonSerializer.Serialize(objValue, _jsonOptions);
                value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse parameter '{key}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Helper to extract an optional parameter with default value
    /// </summary>
    protected T? GetOptionalParameter<T>(
        Dictionary<string, object> parameters,
        string key,
        T? defaultValue = default)
    {
        if (!parameters.TryGetValue(key, out var objValue))
            return defaultValue;

        try
        {
            if (objValue is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), _jsonOptions);
            }
            else if (objValue is T typedValue)
            {
                return typedValue;
            }
            else if (TryConvertValue<T>(objValue, out var converted))
            {
                return converted;
            }
            else
            {
                var json = JsonSerializer.Serialize(objValue, _jsonOptions);
                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Attempts to convert a value to the target type without JSON round-tripping.
    /// Uses TryParse methods to avoid throwing exceptions on invalid conversions.
    /// Returns false if conversion isn't possible (caller falls back to JSON).
    /// </summary>
    private static bool TryConvertValue<T>(object objValue, out T? result)
    {
        result = default;
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        // Numeric source → numeric target (int→long, double→decimal, etc.)
        if (objValue is IConvertible && (targetType.IsPrimitive || targetType == typeof(decimal)))
        {
            var str = objValue.ToString();
            if (str == null) return false;

            if (targetType == typeof(int) && int.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var i))
            { result = (T)(object)i; return true; }
            if (targetType == typeof(long) && long.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var l))
            { result = (T)(object)l; return true; }
            if (targetType == typeof(double) && double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            { result = (T)(object)d; return true; }
            if (targetType == typeof(float) && float.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f))
            { result = (T)(object)f; return true; }
            if (targetType == typeof(decimal) && decimal.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var m))
            { result = (T)(object)m; return true; }
            if (targetType == typeof(bool) && bool.TryParse(str, out var b))
            { result = (T)(object)b; return true; }

            return false;
        }

        // string target
        if (targetType == typeof(string))
        {
            result = (T)(object)objValue.ToString()!;
            return true;
        }

        // List<string> from List<object> containing strings
        if (targetType == typeof(List<string>) && objValue is IEnumerable<object> enumerable)
        {
            var list = enumerable.Select(o => o?.ToString() ?? "").ToList();
            result = (T)(object)list;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Filters the output dictionary to only include keys declared in the YAML returns schema.
    /// Missing keys get type-appropriate defaults. Undeclared keys are silently dropped.
    /// </summary>
    private Dictionary<string, object> FilterOutput(Dictionary<string, object> raw)
    {
        var declaredKeys = _definition.Value.Returns;
        if (declaredKeys == null || declaredKeys.Count == 0)
            return raw;

        var filtered = new Dictionary<string, object>();
        foreach (var (key, fieldDef) in declaredKeys)
        {
            if (raw.TryGetValue(key, out var value))
                filtered[key] = value;
            else
                filtered[key] = GetDefaultForType(fieldDef.Type);
        }
        return filtered;
    }

    private static object GetDefaultForType(string type) => type switch
    {
        "string" => "",
        "number" => 0.0,
        "integer" => 0,
        "boolean" => false,
        "array" => new List<object>(),
        "object" => new Dictionary<string, object>(),
        _ => ""
    };
}
