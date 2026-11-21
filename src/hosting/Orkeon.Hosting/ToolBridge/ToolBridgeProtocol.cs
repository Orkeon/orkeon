using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The wire of the tool bridge (#445, ADR-019): JSON over HTTP under a versioned path, not MCP. Every request
/// carries the token as <c>Authorization: Bearer</c>. Version 1 has five endpoints: the tools the bridge serves
/// (<c>GET tools</c>), a call (<c>POST call</c>), the cancellation of a call in flight (<c>POST cancel</c>), the end
/// of a caller's run (<c>POST end-run</c>) and the heartbeat of a caller with live runs (<c>POST heartbeat</c>). A
/// call names its caller and the caller's run, carries the caller's mounts and the files it reads, and its answer
/// carries the files the tool wrote; a field a side does not know is ignored by the other.
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

    /// <summary>The end of a caller's run on the bridge (<c>POST</c>, a <see cref="BridgeRunRequest"/>).</summary>
    public const string EndRunEndpoint = "end-run";

    /// <summary>The heartbeat of a caller that has runs on the bridge (<c>POST</c>, a <see cref="BridgeHeartbeatRequest"/>).</summary>
    public const string HeartbeatEndpoint = "heartbeat";

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

    /// <summary>
    /// How long the bridge waits for a caller's heartbeat before it ends that caller's runs, in milliseconds
    /// (<c>Orkeon:Bridge:HeartbeatWindow</c>); the caller beats three times in that window.
    /// </summary>
    [JsonPropertyName("heartbeat_window_ms")]
    public long? HeartbeatWindowMs { get; init; }
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

    /// <summary>The id the caller's link picked at its start; the runs and the heartbeat of a caller are under it.</summary>
    [JsonPropertyName("caller_id")]
    public string? CallerId { get; init; }

    /// <summary>The caller's <c>AgentRunScope.RunId</c>; absent for a call outside any run.</summary>
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    /// <summary>
    /// The caller's agent-facing mounts, which the bridge presents over a scratch folder for the length of the call;
    /// absent for a tool that uses no file system or acts on the workspace (it runs on the bridge's own mounts).
    /// </summary>
    [JsonPropertyName("mounts")]
    public ImmutableList<BridgeMount>? Mounts { get; init; }

    /// <summary>The caller's working directory, the one a relative path resolves against.</summary>
    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    /// <summary>The caller's write directory, where a tool writes when it is given no path.</summary>
    [JsonPropertyName("write_directory")]
    public string? WriteDirectory { get; init; }

    /// <summary>The files of the caller the arguments name, placed in the scratch folder before the call.</summary>
    [JsonPropertyName("files")]
    public ImmutableList<BridgeFile>? Files { get; init; }
}

/// <summary>A mount of the caller, presented on the bridge at the same virtual path.</summary>
internal sealed record BridgeMount
{
    /// <summary>The virtual path of the mount.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Whether the caller may write there; a tool on the bridge then may too.</summary>
    [JsonPropertyName("writable")]
    public bool Writable { get; init; }
}

/// <summary>A file of a call: a virtual path of the caller and its bytes (base64 on the wire).</summary>
internal sealed record BridgeFile
{
    /// <summary>The caller's virtual path of the file.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Its content.</summary>
    [JsonPropertyName("content")]
    public required ReadOnlyMemory<byte> Content { get; init; }
}

/// <summary>The body of <c>POST end-run</c>.</summary>
internal sealed record BridgeRunRequest
{
    /// <summary>The caller's id.</summary>
    [JsonPropertyName("caller_id")]
    public required string CallerId { get; init; }

    /// <summary>The run that ended on the caller.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }
}

/// <summary>The body of <c>POST heartbeat</c>.</summary>
internal sealed record BridgeHeartbeatRequest
{
    /// <summary>The caller's id.</summary>
    [JsonPropertyName("caller_id")]
    public required string CallerId { get; init; }
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

    /// <summary>The files the tool wrote in the caller's mounts during the call, which the caller writes on its side.</summary>
    [JsonPropertyName("files")]
    public ImmutableList<BridgeFile>? Files { get; init; }
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
