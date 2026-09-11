using System.Text.Json;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// Adapts an MCP server tool into a Orkeon IBaseTool so it can be
/// registered in the tool registry and used by agents.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class McpToolAdapter : IBaseTool
{
    private readonly McpToolDefinition _definition;
    private readonly McpClient _client;
    private readonly ToolSchema _schema;

    /// <inheritdoc />
    public string Name => _definition.Name;

    /// <inheritdoc />
    public string Description => _definition.Description;

    /// <inheritdoc />
    public ToolSchema Schema => _schema;

    /// <summary>Initializes a new instance of <see cref="McpToolAdapter"/>.</summary>
    /// <param name="definition">The MCP tool definition.</param>
    /// <param name="client">The MCP client used to invoke the tool.</param>
    public McpToolAdapter(McpToolDefinition definition, McpClient client)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _schema = ConvertToToolSchema(definition);
    }

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallCoreAsync();

        async Task<ToolCallResponse> CallCoreAsync()
        {
            JsonElement? arguments = null;
            if (request.Parameters.Count > 0)
            {
                var json = JsonSerializer.Serialize(request.Parameters);
                arguments = JsonElement.Parse(json);
            }

            var result = await _client.CallToolAsync(_definition.Name, arguments, cancellationToken).ConfigureAwait(false);

            if (result.IsError == true)
            {
                var errorText = string.Join("\n", result.Content
                    .Where(c => c.Text != null)
                    .Select(c => c.Text));
                return new ToolCallResponse(false, null, errorText);
            }

            var outputText = string.Join("\n", result.Content
                .Where(c => c.Text != null)
                .Select(c => c.Text));

            return new ToolCallResponse(true, outputText, null);
        }
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> parameters;
        try
        {
            parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(input)
                         ?? [];
        }
        catch (JsonException)
        {
            // If input is not valid JSON, pass it as a single "input" parameter
            parameters = new Dictionary<string, object?> { ["input"] = input };
        }

        var request = new ToolCallRequest(_definition.Name, parameters);
        var response = await CallAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Success)
            return ToolResult.CreateSuccess(response.Result?.ToString() ?? "");

        return ToolResult.CreateError(response.Error ?? "Unknown MCP tool error");
    }

    /// <inheritdoc />
    public bool ValidateInput(string input)
    {
        // Validation is done server-side by the MCP server
        return true;
    }

    /// <summary>
    /// Converts an MCP tool definition (JSON Schema inputSchema) to a Orkeon ToolSchema.
    /// </summary>
    public static ToolSchema ConvertToToolSchema(McpToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var parameters = new Dictionary<string, ParameterSchema>();

        if (definition.InputSchema.HasValue)
        {
            var schema = definition.InputSchema.Value;
            var requiredSet = ExtractRequiredSet(schema);
            PopulateParameters(parameters, schema, requiredSet);
        }

        return new ToolSchema(
            definition.Name,
            definition.Description,
            parameters);
    }

    private static HashSet<string> ExtractRequiredSet(JsonElement schema)
    {
        var requiredSet = new HashSet<string>();
        if (!schema.TryGetProperty("required", out var requiredArray) ||
            requiredArray.ValueKind != JsonValueKind.Array)
            return requiredSet;

        foreach (var item in requiredArray.EnumerateArray())
        {
            if (item.GetString() is string s)
                requiredSet.Add(s);
        }

        return requiredSet;
    }

    private static void PopulateParameters(
        Dictionary<string, ParameterSchema> parameters,
        JsonElement schema,
        HashSet<string> requiredSet)
    {
        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in properties.EnumerateObject())
        {
            parameters[prop.Name] = ConvertProperty(prop.Value, requiredSet.Contains(prop.Name));
        }
    }

    private static ParameterSchema ConvertProperty(JsonElement paramValue, bool isRequired)
    {
        var type = paramValue.TryGetProperty("type", out var typeEl)
            ? typeEl.GetString() ?? "string"
            : "string";

        var description = paramValue.TryGetProperty("description", out var descEl)
            ? descEl.GetString() ?? ""
            : "";

        var defaultValue = ExtractDefaultValue(paramValue);
        var enumValues = ExtractEnumValues(paramValue);

        return new ParameterSchema(type, description, isRequired, defaultValue, enumValues);
    }

    private static object? ExtractDefaultValue(JsonElement paramValue)
    {
        if (!paramValue.TryGetProperty("default", out var defaultEl))
            return null;

        return defaultEl.ValueKind switch
        {
            JsonValueKind.String => defaultEl.GetString(),
            JsonValueKind.Number => defaultEl.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultEl.GetRawText()
        };
    }

    private static List<object> ExtractEnumValues(JsonElement paramValue)
    {
        if (!paramValue.TryGetProperty("enum", out var enumEl) ||
            enumEl.ValueKind != JsonValueKind.Array)
            return [];

        var enumValues = new List<object>();
        foreach (var ev in enumEl.EnumerateArray())
        {
            if (ev.GetString() is string s)
                enumValues.Add(s);
        }
        return enumValues;
    }
}
