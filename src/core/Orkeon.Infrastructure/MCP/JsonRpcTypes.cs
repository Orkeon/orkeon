using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// JSON-RPC 2.0 request message.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class JsonRpcRequest
{
    /// <summary>Gets or sets the JSON-RPC version string.</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the method name to invoke.</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>Gets or sets the method parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    /// <summary>
    /// Gets or sets the request identifier. JSON-RPC allows numbers and strings;
    /// the raw element is kept so external ids round-trip unchanged.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 response message.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class JsonRpcResponse
{
    /// <summary>Gets or sets the JSON-RPC version string.</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the successful result, if any.</summary>
    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    /// <summary>Gets or sets the error, if the request failed.</summary>
    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    /// <summary>
    /// Gets or sets the request identifier this response corresponds to (number or
    /// string — kept as the raw element so external ids round-trip unchanged).
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class JsonRpcError
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

    /// <summary>Initializes a new instance of <see cref="JsonRpcError"/>.</summary>
    public JsonRpcError() { }

    /// <summary>Initializes a new instance of <see cref="JsonRpcError"/> with a code and message.</summary>
    /// <param name="code">The error code.</param>
    /// <param name="message">The error message.</param>
    public JsonRpcError(int code, string message)
    {
        Code = code;
        Message = message;
    }
}

/// <summary>
/// JSON-RPC 2.0 notification (request without id).
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class JsonRpcNotification
{
    /// <summary>Gets or sets the JSON-RPC version string.</summary>
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    /// <summary>Gets or sets the notification method name.</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>Gets or sets the notification parameters.</summary>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// Standard JSON-RPC error codes.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class JsonRpcErrorCodes
{
    /// <summary>Error code for parse errors (-32700).</summary>
    public const int ParseError = -32700;

    /// <summary>Error code for invalid requests (-32600).</summary>
    public const int InvalidRequest = -32600;

    /// <summary>Error code for method not found (-32601).</summary>
    public const int MethodNotFound = -32601;

    /// <summary>Error code for invalid parameters (-32602).</summary>
    public const int InvalidParams = -32602;

    /// <summary>Error code for internal errors (-32603).</summary>
    public const int InternalError = -32603;
}
