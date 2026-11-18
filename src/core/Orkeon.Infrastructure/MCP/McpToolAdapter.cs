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
    /// How many schemas one <c>inputSchema</c> may expand to, its <c>$ref</c> followed: a definition that
    /// names another twice, which names another twice, doubles at each step, and a server could make one
    /// tool cost the memory of the host and the context of every model it is shown to. Beyond it the tool
    /// is set aside, with a warning (#417).
    /// </summary>
    internal const int MaxSchemaNodes = 4096;

    /// <summary>How deep a schema is followed; deeper, a property accepts any JSON value.</summary>
    private const int MaxSchemaDepth = 32;

    /// <summary>
    /// Converts an MCP tool definition (JSON Schema inputSchema) to a Orkeon ToolSchema: every property with
    /// its type, description, default, enum (or <c>const</c>), format and bounds, the items of an array and the
    /// properties of an object at any depth, the alternatives of <c>anyOf</c> / <c>oneOf</c>, a dictionary's
    /// values, <c>additionalProperties: false</c>, and the local <c>$ref</c> resolved.
    /// </summary>
    /// <exception cref="InvalidOperationException">The schema expands beyond <see cref="MaxSchemaNodes"/> schemas.</exception>
    public static ToolSchema ConvertToToolSchema(McpToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.InputSchema.HasValue)
            return new ToolSchema(definition.Name, definition.Description, []);

        var reader = new SchemaReader(definition.InputSchema.Value);
        var root = reader.Resolve(definition.InputSchema.Value);
        return new ToolSchema(definition.Name, definition.Description, reader.ReadProperties(root, 0) ?? [])
        {
            AllowsAdditionalParameters = !IsFalse(root, "additionalProperties"),
        };
    }

    /// <summary>
    /// Reads one remote schema into <see cref="ParameterSchema"/>s. Nothing here throws on a schema it does not
    /// expect (#417): what it cannot read accepts any JSON value. Only a schema that expands beyond
    /// <see cref="MaxSchemaNodes"/> fails.
    /// </summary>
    private sealed class SchemaReader(JsonElement root)
    {
        private readonly HashSet<string> _expanding = new(StringComparer.Ordinal);
        private int _nodes;

        /// <summary>The schema a root <c>$ref</c> names, followed; the element itself when it is no reference or names nothing.</summary>
        public JsonElement Resolve(JsonElement schema)
        {
            for (var hops = 0; hops < MaxSchemaDepth && ReadString(schema, "$ref") is { } reference; hops++)
            {
                if (Lookup(reference) is not { } target)
                    break;
                schema = target;
            }

            return schema;
        }

        /// <summary>The <c>properties</c> of an object schema, each with its <c>required</c>; null when it declares none.</summary>
        public Dictionary<string, ParameterSchema>? ReadProperties(JsonElement schema, int depth)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
                return null;

            var required = ExtractRequiredSet(schema);
            var result = new Dictionary<string, ParameterSchema>();
            foreach (var property in properties.EnumerateObject())
                result[property.Name] = Read(property.Value, required.Contains(property.Name), depth + 1);
            return result;
        }

        private ParameterSchema Read(JsonElement schema, bool required, int depth)
        {
            if (++_nodes > MaxSchemaNodes)
                throw new InvalidOperationException($"the inputSchema expands beyond {MaxSchemaNodes} schemas");

            // A boolean schema (`true`), anything that is no object, or a schema too deep: any JSON value.
            if (schema.ValueKind != JsonValueKind.Object || depth > MaxSchemaDepth)
                return AnyValue(schema, required);

            // A reference is the schema it names, with the words it carries itself; one that names nothing, or
            // that is being expanded already (a tree whose node holds nodes), accepts any JSON value.
            if (ReadString(schema, "$ref") is { } reference)
            {
                if (Lookup(reference) is not { } target || !_expanding.Add(reference))
                    return AnyValue(schema, required);
                try
                {
                    return WithOwnWords(schema, Read(target, required, depth + 1));
                }
                finally
                {
                    _expanding.Remove(reference);
                }
            }

            // `allOf` of one schema, and the `anyOf: [X, {"type": "null"}]` of a nullable parameter: X.
            if (SingleSchemaOf(schema) is { } single)
                return WithOwnWords(schema, Read(single, required, depth + 1));

            var items = ReadItems(schema, depth, out var maxItemsOfTuple);
            var alternatives = NonNullAlternatives(schema);
            var properties = ReadProperties(schema, depth);
            var values = schema.TryGetProperty("additionalProperties", out var valuesEl) && valuesEl.ValueKind == JsonValueKind.Object
                ? Read(valuesEl, required: false, depth + 1)
                : null;
            var (minimum, exclusiveMinimum) = ReadBound(schema, "minimum", "exclusiveMinimum");
            var (maximum, exclusiveMaximum) = ReadBound(schema, "maximum", "exclusiveMaximum");
            // A schema with `properties` and no `type` member is an object; one whose `type` names another or
            // several publishes no properties: they would describe what it is not.
            var type = schema.TryGetProperty("type", out _) ? ReadType(schema) : properties is not null ? "object" : null;

            return new ParameterSchema(
                type,
                ReadString(schema, "description") ?? "",
                required,
                ExtractDefaultValue(schema),
                ExtractEnumValues(schema),
                Format: ReadString(schema, "format"),
                ItemsType: items?.Type,
                ItemsFormat: items?.Format,
                Values: values)
            {
                Items = items,
                // The properties of object items only: a string item that carries `properties` publishes none.
                ItemProperties = items is { Type: "object" } ? items.Properties : null,
                Properties = type == "object" && properties is { Count: > 0 } ? properties : null,
                AnyOf = alternatives.Count > 0 ? [.. alternatives.Select(a => Read(a, required: false, depth + 1))] : null,
                AllowsAdditionalProperties = !IsFalse(schema, "additionalProperties"),
                Minimum = minimum,
                Maximum = maximum,
                ExclusiveMinimum = exclusiveMinimum,
                ExclusiveMaximum = exclusiveMaximum,
                MinLength = ReadCount(schema, "minLength"),
                MaxLength = ReadCount(schema, "maxLength"),
                Pattern = ReadString(schema, "pattern"),
                MinItems = ReadCount(schema, "minItems"),
                MaxItems = ReadCount(schema, "maxItems") ?? maxItemsOfTuple,
            };
        }

        /// <summary>
        /// The schema of an array's items: <c>items</c> as an object; a tuple (<c>items</c> as an array, or
        /// <c>prefixItems</c>) as the alternatives of its positions, the position itself lost; null when they
        /// are not known. A tuple closed by <c>items: false</c>, or <c>items: false</c> alone, bounds the count.
        /// </summary>
        private ParameterSchema? ReadItems(JsonElement schema, int depth, out int? maxItems)
        {
            maxItems = null;
            var hasItems = schema.TryGetProperty("items", out var items);
            var tuple = hasItems && items.ValueKind == JsonValueKind.Array
                ? items
                : schema.TryGetProperty("prefixItems", out var prefix) && prefix.ValueKind == JsonValueKind.Array ? prefix : (JsonElement?)null;

            if (hasItems && items.ValueKind == JsonValueKind.False)
                maxItems = tuple?.GetArrayLength() ?? 0;
            if (hasItems && items.ValueKind == JsonValueKind.Object)
                return Read(items, required: false, depth + 1);
            if (tuple is not { } positions || positions.GetArrayLength() == 0)
                return null;

            var read = positions.EnumerateArray().Select(p => Read(p, required: false, depth + 1)).ToList();
            return read.Count == 1 ? read[0] : new ParameterSchema(null, "", false) { AnyOf = read };
        }

        /// <summary>The schema a local <c>$ref</c> (<c>#</c>, <c>#/$defs/X</c>, any JSON pointer of the document) names; null for any other.</summary>
        private JsonElement? Lookup(string reference)
        {
            if (!reference.StartsWith('#'))
                return null;

            var node = root;
            foreach (var raw in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(token, out var child))
                    node = child;
                else if (node.ValueKind == JsonValueKind.Array && int.TryParse(token, out var index) && index >= 0 && index < node.GetArrayLength())
                    node = node[index];
                else
                    return null;
            }

            return node;
        }
    }

    /// <summary>A parameter of any JSON value, with the description its schema carries.</summary>
    private static ParameterSchema AnyValue(JsonElement schema, bool required) =>
        new(null, schema.ValueKind == JsonValueKind.Object ? ReadString(schema, "description") ?? "" : "", required);

    /// <summary>What a schema that only points at another says itself - its description, default, format - over what the other says.</summary>
    private static ParameterSchema WithOwnWords(JsonElement schema, ParameterSchema read) => read with
    {
        Description = ReadString(schema, "description") ?? read.Description,
        Default = ExtractDefaultValue(schema) ?? read.Default,
        Format = ReadString(schema, "format") ?? read.Format,
    };

    /// <summary>
    /// The one schema a schema stands for when it has no type nor properties of its own: the only member of
    /// its <c>allOf</c>, or the only alternative of its <c>anyOf</c> / <c>oneOf</c> that is not
    /// <c>{"type": "null"}</c>. Null when there is none or several.
    /// </summary>
    private static JsonElement? SingleSchemaOf(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out _) || schema.TryGetProperty("properties", out _))
            return null;

        var parts = new List<JsonElement>();
        if (schema.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            if (all.GetArrayLength() != 1)
                return null;
            parts.Add(all[0]);
        }

        parts.AddRange(NonNullAlternatives(schema));
        return parts.Count == 1 ? parts[0] : null;
    }

    /// <summary>The alternatives of <c>anyOf</c> and <c>oneOf</c> but <c>{"type": "null"}</c>: a value may be absent anyway. <c>oneOf</c> is read as <c>anyOf</c>.</summary>
    private static List<JsonElement> NonNullAlternatives(JsonElement schema)
    {
        var alternatives = new List<JsonElement>();
        foreach (var keyword in (string[])["anyOf", "oneOf"])
        {
            if (!schema.TryGetProperty(keyword, out var list) || list.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var alternative in list.EnumerateArray())
            {
                if (ReadString(alternative, "type") != "null")
                    alternatives.Add(alternative);
            }
        }

        return alternatives;
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

    private static bool IsFalse(JsonElement schema, string member) =>
        schema.ValueKind == JsonValueKind.Object &&
        schema.TryGetProperty(member, out var value) &&
        value.ValueKind == JsonValueKind.False;

    /// <summary>A finite number member as a double; null when it is absent, no number, or no double holds it.</summary>
    private static double? ReadNumber(JsonElement schema, string member) =>
        schema.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.GetDouble() is var number && double.IsFinite(number)
            ? number
            : null;

    /// <summary>A count (<c>minLength</c>, <c>maxItems</c>): a whole number from 0 to <see cref="int.MaxValue"/>; null for any other.</summary>
    private static int? ReadCount(JsonElement schema, string member) =>
        schema.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var count) && count is >= 0 and <= int.MaxValue
            ? (int)count
            : null;

    /// <summary>
    /// A bound and its exclusive form. The exclusive one is a number since draft 06; in draft 04 it is a
    /// boolean that makes the bound itself exclusive.
    /// </summary>
    private static (double? Inclusive, double? Exclusive) ReadBound(JsonElement schema, string inclusive, string exclusive)
    {
        var bound = ReadNumber(schema, inclusive);
        if (schema.TryGetProperty(exclusive, out var flag))
        {
            if (flag.ValueKind == JsonValueKind.Number)
                return (bound, ReadNumber(schema, exclusive));
            if (flag.ValueKind == JsonValueKind.True)
                return (null, bound);
        }

        return (bound, null);
    }

    /// <summary>A JSON number as a long when it is one, else a double; null when no double holds it (<c>1e400</c>), since infinity cannot be written back as JSON.</summary>
    private static object? ReadFiniteNumber(JsonElement number)
    {
        if (number.TryGetInt64(out var whole))
            return whole;
        var value = number.GetDouble();
        return double.IsFinite(value) ? value : null;
    }

    /// <summary>A string member of an object; null when it is absent, no string, or the element is no object.</summary>
    private static string? ReadString(JsonElement schema, string member) =>
        schema.ValueKind == JsonValueKind.Object &&
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
            // A whole number stays one (a long), and a number no double holds is no default.
            JsonValueKind.Number => ReadFiniteNumber(defaultEl),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // `"default": null` (what a nullable parameter often declares) is no default, never the text "null".
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            // An array or an object is kept as the JSON value it is, never as its text.
            _ => defaultEl.Clone()
        };
    }

    /// <summary>The scalar values of an <c>enum</c> (or of a <c>const</c>), as they are: a string, a number (a long when it is one, else a double) or a boolean.</summary>
    private static List<object> ExtractEnumValues(JsonElement paramValue)
    {
        // `const` is an enum of one value.
        IEnumerable<JsonElement> listed;
        if (paramValue.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
            listed = enumEl.EnumerateArray();
        else if (paramValue.TryGetProperty("const", out var constEl))
            listed = [constEl];
        else
            return [];

        var enumValues = new List<object>();
        foreach (var ev in listed)
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
