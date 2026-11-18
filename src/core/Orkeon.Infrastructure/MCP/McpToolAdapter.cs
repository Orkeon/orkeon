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

    /// <summary>
    /// The access class the server's annotations give the tool, for a server the operator trusts
    /// (<see cref="McpServerConfig.TrustAnnotations"/>): <see cref="ToolAccess.Read"/> for
    /// <c>readOnlyHint: true</c>, <see cref="ToolAccess.Execute"/> for annotations without it. The
    /// annotations of an untrusted server are a claim any server can make — a destructive tool labelled
    /// read-only would pass <c>plan</c> mode — so they are ignored there, and the tool reports
    /// <see cref="ToolAccess.Unspecified"/>, as it does when the server sends none: the permission
    /// gate keeps its fail-closed classification.
    /// </summary>
    public ToolAccess Access => _definition.Annotations switch
    {
        null => ToolAccess.Unspecified,
        _ when !_trustAnnotations => ToolAccess.Unspecified,
        { ReadOnlyHint: true } => ToolAccess.Read,
        _ => ToolAccess.Execute,
    };

    private readonly bool _trustAnnotations;
    private readonly TimeSpan? _callTimeout;

    /// <summary>
    /// Run before a call and outside <c>CallTimeout</c>: the sign-in a person may owe (the device flow) is bounded
    /// by the lifetime of its code, not by the time one call may take.
    /// </summary>
    internal Func<CancellationToken, ValueTask>? BeforeCall { get; init; }

    /// <summary>Initializes a new instance of <see cref="McpToolAdapter"/>.</summary>
    /// <param name="definition">The MCP tool definition.</param>
    /// <param name="client">The MCP client used to invoke the tool.</param>
    /// <param name="trustAnnotations">
    /// Whether the server's <c>annotations</c> decide <see cref="Access"/>; <see langword="false"/> (the
    /// default) leaves every tool of the server at <see cref="ToolAccess.Unspecified"/>.
    /// </param>
    /// <param name="callTimeout">
    /// How long one call may take (<see cref="McpServerConfig.CallTimeout"/>); <see langword="null"/> for no bound.
    /// </param>
    public McpToolAdapter(McpToolDefinition definition, McpClient client, bool trustAnnotations = false, TimeSpan? callTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _trustAnnotations = trustAnnotations;
        _callTimeout = callTimeout is { } t && t > TimeSpan.Zero ? t : null;
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

            if (BeforeCall is { } beforeCall)
                await beforeCall(cancellationToken).ConfigureAwait(false);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_callTimeout is { } timeout)
                linked.CancelAfter(timeout);

            McpToolCallResult result;
            try
            {
                result = await _client.CallToolAsync(_definition.Name, arguments, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _callTimeout is not null)
            {
                var timedOut = new McpCallTimeoutException(_definition.Name, _callTimeout.Value);
                return new ToolCallResponse(false, null, timedOut.Message,
                    new Dictionary<string, object?> { ["error_type"] = nameof(McpCallTimeoutException) });
            }

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
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("required", out var requiredArray) ||
            requiredArray.ValueKind != JsonValueKind.Array)
            return requiredSet;

        // A remote schema is read, never trusted: an entry that is no string is skipped (#417).
        foreach (var item in requiredArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is string s)
                requiredSet.Add(s);
        }

        return requiredSet;
    }

    private static void PopulateParameters(
        Dictionary<string, ParameterSchema> parameters,
        JsonElement schema,
        HashSet<string> requiredSet)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return;

        foreach (var prop in properties.EnumerateObject())
        {
            parameters[prop.Name] = ConvertProperty(prop.Value, requiredSet.Contains(prop.Name));
        }
    }

    /// <summary>
    /// One property of a remote schema. Kept: <c>type</c>, <c>description</c>, <c>default</c>, <c>enum</c>,
    /// <c>format</c>, the <c>items</c> of an array (their type, format, and the properties of an array of objects)
    /// and the <c>additionalProperties</c> of a dictionary (#415). A property with no single type - none at all,
    /// a bare <c>anyOf</c> or <c>$ref</c>, a <c>type</c> array naming several - has a null type: any JSON value.
    /// Nothing here throws on a schema it does not expect (#417).
    /// </summary>
    private static ParameterSchema ConvertProperty(JsonElement paramValue, bool isRequired)
    {
        // A boolean schema (`true`), or anything that is no object: a parameter of any value.
        if (paramValue.ValueKind != JsonValueKind.Object)
            return new ParameterSchema(null, "", isRequired);

        var items = paramValue.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object
            ? itemsEl
            : (JsonElement?)null;
        var values = paramValue.TryGetProperty("additionalProperties", out var valuesEl) && valuesEl.ValueKind == JsonValueKind.Object
            ? ConvertProperty(valuesEl, isRequired: false)
            : null;

        return new ParameterSchema(
            ReadType(paramValue),
            ReadString(paramValue, "description") ?? "",
            isRequired,
            ExtractDefaultValue(paramValue),
            ExtractEnumValues(paramValue),
            Format: ReadString(paramValue, "format"),
            ItemsType: items is { } typed ? ReadItemsType(typed) : null,
            ItemsFormat: items is { } formatted ? ReadString(formatted, "format") : null,
            Values: values)
        {
            ItemProperties = items is { } described ? ReadItemProperties(described) : null
        };
    }

    /// <summary>The properties of the items of an array of objects, each with its own <c>required</c>; null when the items declare none.</summary>
    private static Dictionary<string, ParameterSchema>? ReadItemProperties(JsonElement items)
    {
        var properties = new Dictionary<string, ParameterSchema>();
        PopulateParameters(properties, items, ExtractRequiredSet(items));
        return properties.Count > 0 ? properties : null;
    }

    /// <summary>
    /// The <c>type</c> of a schema: the string itself, or the one member of a <c>type</c> array that is not
    /// <c>"null"</c> (a nullable parameter). Null - any JSON value - when there is none or several.
    /// </summary>
    private static string? ReadType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeEl))
            return null;
        if (typeEl.ValueKind == JsonValueKind.String)
            return typeEl.GetString();
        if (typeEl.ValueKind != JsonValueKind.Array)
            return null;

        string? single = null;
        foreach (var member in typeEl.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.String)
                return null;
            var name = member.GetString();
            if (name is null or "null")
                continue;
            if (single is not null && single != name)
                return null;
            single = name;
        }

        return single;
    }

    /// <summary>The type of an array's items; items that declare <c>properties</c> and no type are objects, so their properties are published.</summary>
    private static string? ReadItemsType(JsonElement items) =>
        ReadType(items) ?? (items.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object ? "object" : null);

    /// <summary>A JSON number as a long when it is one, else a double; null when no double holds it (<c>1e400</c>), since infinity cannot be written back as JSON.</summary>
    private static object? ReadFiniteNumber(JsonElement number)
    {
        if (number.TryGetInt64(out var whole))
            return whole;
        var value = number.GetDouble();
        return double.IsFinite(value) ? value : null;
    }

    private static string? ReadString(JsonElement schema, string member) =>
        schema.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static object? ExtractDefaultValue(JsonElement paramValue)
    {
        if (!paramValue.TryGetProperty("default", out var defaultEl))
            return null;

        return defaultEl.ValueKind switch
        {
            JsonValueKind.String => defaultEl.GetString(),
            JsonValueKind.Number => double.IsFinite(defaultEl.GetDouble()) ? defaultEl.GetDouble() : null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultEl.GetRawText()
        };
    }

    /// <summary>The scalar values of an <c>enum</c>, as they are: a string, a number (a long when it is one, else a double) or a boolean.</summary>
    private static List<object> ExtractEnumValues(JsonElement paramValue)
    {
        if (!paramValue.TryGetProperty("enum", out var enumEl) ||
            enumEl.ValueKind != JsonValueKind.Array)
            return [];

        var enumValues = new List<object>();
        foreach (var ev in enumEl.EnumerateArray())
        {
            switch (ev.ValueKind)
            {
                case JsonValueKind.String:
                    enumValues.Add(ev.GetString()!);
                    break;
                case JsonValueKind.Number:
                    if (ReadFiniteNumber(ev) is { } number)
                        enumValues.Add(number);
                    break;
                case JsonValueKind.True:
                    enumValues.Add(true);
                    break;
                case JsonValueKind.False:
                    enumValues.Add(false);
                    break;
            }
        }
        return enumValues;
    }
}
