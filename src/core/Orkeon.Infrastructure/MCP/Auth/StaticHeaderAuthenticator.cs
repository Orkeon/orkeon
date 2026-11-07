namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>Adds fixed headers, resolved once when the server is configured.</summary>
internal sealed class StaticHeaderAuthenticator(IReadOnlyDictionary<string, string> headers) : IMcpRequestAuthenticator
{
    public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var (name, value) in headers)
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return ValueTask.CompletedTask;
    }
}
