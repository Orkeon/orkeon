using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// Puts a server's credentials on one outgoing MCP HTTP request. Applied per request, so the HTTP
/// client the factory hands out is shared by every server and never carries a token itself.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IMcpRequestAuthenticator
{
    /// <summary>Adds the credentials to <paramref name="request"/>.</summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="ct">The cancellation token.</param>
    ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default);
}
