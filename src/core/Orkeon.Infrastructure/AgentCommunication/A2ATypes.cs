using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Wire protocol message envelope for A2A communication.
/// Follows JSON-RPC style request/response pattern similar to MCP's <c>JsonRpcTypes</c>.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class A2AMessage
{
    /// <summary>Gets or sets the JSON-RPC version string.</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the method name (e.g., "tasks/send", "tasks/cancel").</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>Gets or sets the request identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the message parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    /// <summary>Gets or sets the result, if this is a response.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    /// <summary>Gets or sets the error, if the request failed.</summary>
    [JsonPropertyName("error")]
    public A2AError? Error { get; set; }
}

/// <summary>
/// Error object for A2A wire protocol, following JSON-RPC error conventions.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class A2AError
{
    /// <summary>Gets or sets the numeric error code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>Gets or sets the human-readable error message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>Gets or sets optional additional error data.</summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>Initializes a new instance of <see cref="A2AError"/>.</summary>
    public A2AError() { }

    /// <summary>Initializes a new instance of <see cref="A2AError"/> with a code and message.</summary>
    public A2AError(int code, string message)
    {
        Code = code;
        Message = message;
    }
}

/// <summary>
/// Standard A2A error codes.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class A2AErrorCodes
{
    /// <summary>The request payload could not be parsed.</summary>
    public const int ParseError = -32700;

    /// <summary>The request is not valid.</summary>
    public const int InvalidRequest = -32600;

    /// <summary>The requested method was not found.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>The provided parameters are invalid.</summary>
    public const int InvalidParams = -32602;

    /// <summary>An internal server error occurred.</summary>
    public const int InternalError = -32603;

    /// <summary>The requested task was not found.</summary>
    public const int TaskNotFound = -32001;

    /// <summary>The requested skill was not found.</summary>
    public const int SkillNotFound = -32002;
}
