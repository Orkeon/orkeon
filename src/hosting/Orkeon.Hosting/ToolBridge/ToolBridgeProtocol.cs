using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The wire of the tool bridge (#445, ADR-019): JSON over HTTP under a versioned path, not MCP. Every request
/// carries the token as <c>Authorization: Bearer</c>. Version 1 has three endpoints: the tools the bridge serves
/// (<c>GET tools</c>), a call (<c>POST call</c>) and the cancellation of a call in flight (<c>POST cancel</c>). A
/// later version adds what a run needs (its id on a call, its end, a heartbeat) and the files of a call as new
/// fields and endpoints beside these; a field a side does not know is ignored by the other.
/// </summary>
internal static class ToolBridgeProtocol
{
    /// <summary>The path every endpoint of version 1 sits under, with its final slash.</summary>
    public const string BasePath = "/orkeon-bridge/v1/";

    /// <summary>The tools the bridge serves, each with its contract hash (<c>GET</c>).</summary>
    public const string ToolsEndpoint = "tools";

    /// <summary>One call of a served tool (<c>POST</c>, a <see cref="BridgeCallRequest"/>).</summary>
    public const string CallEndpoint = "call";

    /// <summary>The cancellation of a call in flight (<c>POST</c>, a <see cref="BridgeCancelRequest"/>).</summary>
    public const string CancelEndpoint = "cancel";

    /// <summary>The scheme of the token header.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The audit caller a call the bridge runs is recorded under.</summary>
    public const string AuditCaller = "bridge";

    /// <summary>The default environment variable both sides read the token from.</summary>
    public const string DefaultTokenEnvVar = "ORKEON_BRIDGE_TOKEN";

    /// <summary>The serializer settings of every body, both ways.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth,
        WriteIndented = false,
    };

    /// <summary>
    /// A JSON value read off the wire as the plain values a tool takes and returns: a string, a <see cref="long"/> or
    /// a <see cref="double"/>, a <see cref="bool"/>, null, a dictionary of them or a list of them.
    /// </summary>
    /// <param name="element">The value.</param>
    /// <returns>Its plain form.</returns>
    public static object? ToPlain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object => ToPlainObject(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ToPlain).ToList(),
        _ => null,
    };

    /// <summary>The members of a JSON object, each in its plain form; an empty dictionary for any other value.</summary>
    /// <param name="element">The object.</param>
    /// <returns>Its members.</returns>
    public static Dictionary<string, object?> ToPlainObject(JsonElement element)
    {
        var members = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object)
            return members;
        foreach (var property in element.EnumerateObject())
            members[property.Name] = ToPlain(property.Value);
        return members;
    }

    /// <summary>A size in bytes as an operator reads it.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The size with its unit.</returns>
    public static string Describe(long bytes) => bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
}

/// <summary>One tool a bridge serves.</summary>
internal sealed record BridgeServedTool
{
    /// <summary>The tool's name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The hash of its schema and of its access table (<see cref="ToolBridgeContract"/>).</summary>
    [JsonPropertyName("contract_hash")]
    public required string ContractHash { get; init; }
}

/// <summary>The answer of <c>GET tools</c>.</summary>
internal sealed record BridgeToolListResponse
{
    /// <summary>The tools the bridge serves, by name.</summary>
    [JsonPropertyName("tools")]
    public required ImmutableList<BridgeServedTool> Tools { get; init; }
}

/// <summary>The body of <c>POST call</c>.</summary>
internal sealed record BridgeCallRequest
{
    /// <summary>An id the caller picks for the call, which <c>POST cancel</c> names.</summary>
    [JsonPropertyName("call_id")]
    public required string CallId { get; init; }

    /// <summary>The name of the tool.</summary>
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    /// <summary>The arguments of the call, as the model gave them.</summary>
    [JsonPropertyName("arguments")]
    public required JsonElement Arguments { get; init; }

    /// <summary>The call's context string, when the caller's request carries one.</summary>
    [JsonPropertyName("context")]
    public string? Context { get; init; }
}

/// <summary>The answer of <c>POST call</c>: the tool's raw response, untruncated and unsanitized.</summary>
internal sealed record BridgeCallResponse
{
    /// <summary>Whether the tool succeeded.</summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>The tool's result, as JSON; absent when it returned none.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; init; }

    /// <summary>The tool's error, on a failure.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>The tool's metadata, as JSON; the entries that do not serialize are left out.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Whether the tool bounded its own output (<c>ToolCallResponse.AlreadyBounded</c>).</summary>
    [JsonPropertyName("already_bounded")]
    public bool AlreadyBounded { get; init; }
}

/// <summary>The body of <c>POST cancel</c>.</summary>
internal sealed record BridgeCancelRequest
{
    /// <summary>The id the call was sent with.</summary>
    [JsonPropertyName("call_id")]
    public required string CallId { get; init; }
}

/// <summary>The body of every refusal: what was refused and why.</summary>
internal sealed record BridgeErrorResponse
{
    /// <summary>The reason, in the operator's words.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }
}
