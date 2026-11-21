using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>
/// The wire of the tool bridge (#445, ADR-019): JSON over HTTP under a versioned path, not MCP. Every request
/// carries the token as <c>Authorization: Bearer</c>. Version 1 has six endpoints: the tools the bridge serves
/// (<c>GET tools</c>), the opening of a caller's session (<c>POST session</c>), a call (<c>POST call</c>), the
/// cancellation of a call in flight (<c>POST cancel</c>), the end of a caller's run (<c>POST end-run</c>) and the
/// heartbeat of a session (<c>POST heartbeat</c>). The last four carry the session id the bridge issued in the header
/// <see cref="SessionHeader"/>; a session the bridge does not hold, unknown or expired, is answered
/// <see cref="SessionLostStatus"/>. A call names the caller's run, carries the caller's mounts and the files it reads,
/// and its answer carries the files the tool wrote; a field a side does not know is ignored by the other.
/// </summary>
internal static class ToolBridgeProtocol
{
    /// <summary>The path every endpoint of version 1 sits under, with its final slash.</summary>
    public const string BasePath = "/orkeon-bridge/v1/";

    /// <summary>The tools the bridge serves, each with its contract hash (<c>GET</c>).</summary>
    public const string ToolsEndpoint = "tools";

    /// <summary>The opening of a caller's session (<c>POST</c>, an empty object; a <see cref="BridgeSessionResponse"/>).</summary>
    public const string SessionEndpoint = "session";

    /// <summary>The header that carries the session id on every request of a session.</summary>
    public const string SessionHeader = "X-Orkeon-Bridge-Session";

    /// <summary>The status of a request whose session the bridge does not hold: unknown, expired or ended.</summary>
    public const System.Net.HttpStatusCode SessionLostStatus = System.Net.HttpStatusCode.Gone;

    /// <summary>One call of a served tool (<c>POST</c>, a <see cref="BridgeCallRequest"/>).</summary>
    public const string CallEndpoint = "call";

    /// <summary>The cancellation of a call in flight (<c>POST</c>, a <see cref="BridgeCancelRequest"/>).</summary>
    public const string CancelEndpoint = "cancel";

    /// <summary>The end of a caller's run on the bridge (<c>POST</c>, a <see cref="BridgeRunRequest"/>).</summary>
    public const string EndRunEndpoint = "end-run";

    /// <summary>The heartbeat of a session (<c>POST</c>, an empty object).</summary>
    public const string HeartbeatEndpoint = "heartbeat";

    /// <summary>The scheme of the token header.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The audit caller a call the bridge runs is recorded under.</summary>
    public const string AuditCaller = "bridge";

    /// <summary>The default environment variable both sides read the token from.</summary>
    public const string DefaultTokenEnvVar = "ORKEON_BRIDGE_TOKEN";

    /// <summary>The shortest token either side accepts, in characters, once trimmed.</summary>
    public const int MinTokenLength = 16;

    /// <summary>
    /// The token read from <paramref name="variable"/>, trimmed as the bridge trims the one a request presents, or the
    /// reason it cannot be used: unset, or shorter than <see cref="MinTokenLength"/>. The token is never in the reason.
    /// </summary>
    /// <param name="value">The variable's value, null when unset.</param>
    /// <param name="variable">The variable's name, for the reason.</param>
    /// <param name="problem">The reason the token is refused; null when it is accepted.</param>
    /// <returns>The trimmed token, or null when it is refused.</returns>
    public static string? ReadToken(string? value, string variable, out string? problem)
    {
        var token = value?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            problem = $"it reads its token from the environment variable {variable}, which is not set.";
            return null;
        }

        if (token.Length < MinTokenLength)
        {
            problem = $"the token in the environment variable {variable} is shorter than {MinTokenLength} characters: use a long random value (openssl rand -hex 32).";
            return null;
        }

        problem = null;
        return token;
    }

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
    /// How long the bridge waits for a sign of a caller's session before it ends the session and its runs, in
    /// milliseconds (<c>Orkeon:Bridge:HeartbeatWindow</c>); the caller beats three times in that window.
    /// </summary>
    [JsonPropertyName("heartbeat_window_ms")]
    public long? HeartbeatWindowMs { get; init; }
}

/// <summary>The body of <c>POST call</c>.</summary>
internal sealed record BridgeCallRequest
{
    /// <summary>An id the caller picks for the call, unique within its session, which <c>POST cancel</c> names.</summary>
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

/// <summary>The body of <c>POST end-run</c>; the run is one of the session the header names.</summary>
internal sealed record BridgeRunRequest
{
    /// <summary>The run that ended on the caller.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }
}

/// <summary>The body of <c>POST session</c> and <c>POST heartbeat</c>: an empty object.</summary>
internal sealed record BridgeEmptyRequest;

/// <summary>The answer of <c>POST session</c>.</summary>
internal sealed record BridgeSessionResponse
{
    /// <summary>
    /// The session id: random, unguessable, sent back in <see cref="ToolBridgeProtocol.SessionHeader"/>; a bearer
    /// secret of the caller, like the token.
    /// </summary>
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    /// <summary>How long the bridge keeps the session without a sign of it, in milliseconds.</summary>
    [JsonPropertyName("heartbeat_window_ms")]
    public long? HeartbeatWindowMs { get; init; }
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
