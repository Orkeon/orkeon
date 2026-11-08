using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// What a host adds to the server beyond the registry's tools (FWK-24): tools it runs itself (a crew
/// exposed by <c>orkeon mcp serve --crew</c>) and prompts. The server lists them with its own, routes
/// a <c>tools/call</c> or a <c>prompts/get</c> that names one to the extension, and advertises the
/// <c>prompts</c> capability when the extension has any.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IMcpServerExtension
{
    /// <summary>Gets the tools the extension serves; their names never collide with a registry tool's.</summary>
    IReadOnlyList<McpToolDefinition> Tools { get; }

    /// <summary>Gets the prompts the extension serves.</summary>
    IReadOnlyList<McpPrompt> Prompts { get; }

    /// <summary>Gets whether <paramref name="name"/> is one of <see cref="Tools"/>.</summary>
    /// <param name="name">The tool's name.</param>
    bool OwnsTool(string name);

    /// <summary>
    /// Called when the server's input has ended: nobody is left to send new requests, and the calls that wait for their
    /// turn (a crew busy with another call) are let go without running, answered an error result. A call already
    /// running finishes.
    /// </summary>
    void StopAdmitting();

    /// <summary>
    /// Runs the tool <paramref name="name"/>. A refusal of the arguments throws <see cref="McpRequestException"/>
    /// (answered as a JSON-RPC error); a failure of the run itself is a result with <c>isError</c> set.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="arguments">The arguments of the call, as the client sent them.</param>
    /// <param name="progress">Where the run reports its progress; a no-op when the client sent no <c>progressToken</c>.</param>
    /// <param name="ct">Cancelled when the client cancels the request.</param>
    /// <returns>The result of the call.</returns>
    Task<McpToolCallResult> CallToolAsync(string name, JsonElement? arguments, IMcpProgress progress, CancellationToken ct);

    /// <summary>Gets the prompt <paramref name="name"/> with its arguments filled in; an unknown name or a missing argument throws <see cref="McpRequestException"/>.</summary>
    /// <param name="name">The prompt's name.</param>
    /// <param name="arguments">The arguments of the request, as the client sent them.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The prompt's messages.</returns>
    Task<McpPromptGetResult> GetPromptAsync(string name, JsonElement? arguments, CancellationToken ct);
}

/// <summary>Where a long call reports how far it is (<c>notifications/progress</c>).</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IMcpProgress
{
    /// <summary>Reports <paramref name="progress"/>, out of <paramref name="total"/> when known. Values that do not increase are dropped.</summary>
    /// <param name="progress">How far the call is; it only grows.</param>
    /// <param name="total">The value <paramref name="progress"/> ends at, when known.</param>
    /// <param name="message">What just happened, when worth saying.</param>
    void Report(double progress, double? total = null, string? message = null);
}

/// <summary>A request the server refuses with a JSON-RPC error: what an extension throws for arguments it does not accept.</summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class McpRequestException : Exception
{
    /// <summary>Initializes a refusal with the JSON-RPC error <paramref name="code"/> (<see cref="JsonRpcErrorCodes.InvalidParams"/> for a bad argument).</summary>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The reason, shown to the client.</param>
    public McpRequestException(int code, string message) : base(message) => Code = code;

    /// <summary>Initializes an invalid-params refusal with no reason.</summary>
    public McpRequestException() : this(JsonRpcErrorCodes.InvalidParams, "Invalid params.")
    {
    }

    /// <summary>Initializes an invalid-params refusal.</summary>
    /// <param name="message">The reason, shown to the client.</param>
    public McpRequestException(string message) : this(JsonRpcErrorCodes.InvalidParams, message)
    {
    }

    /// <summary>Initializes an invalid-params refusal that wraps another failure.</summary>
    /// <param name="message">The reason, shown to the client.</param>
    /// <param name="innerException">The failure behind it.</param>
    public McpRequestException(string message, Exception innerException) : base(message, innerException) => Code = JsonRpcErrorCodes.InvalidParams;

    /// <summary>Gets the JSON-RPC error code.</summary>
    public int Code { get; }
}
