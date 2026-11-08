namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// Adds fixed headers, resolved once when the server is configured. A header the request already carries is the
/// transport's and is left as it is: a fixed value never overwrites what the transport computed.
/// </summary>
internal sealed class StaticHeaderAuthenticator(IReadOnlyDictionary<string, string> headers) : IMcpRequestAuthenticator
{
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var (name, value) in headers)
        {
            if (!request.Headers.Contains(name))
                request.Headers.TryAddWithoutValidation(name, value);
        }

        return ValueTask.CompletedTask;
    }
}
