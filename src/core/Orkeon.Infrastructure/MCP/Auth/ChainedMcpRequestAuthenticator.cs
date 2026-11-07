namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>Applies several authenticators in order.</summary>
internal sealed class ChainedMcpRequestAuthenticator(IReadOnlyList<IMcpRequestAuthenticator> links) : IMcpRequestAuthenticator
{
    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        foreach (var link in links)
            await link.ApplyAsync(request, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> HandleUnauthorizedAsync(string? rejectedCredential, CancellationToken ct = default)
    {
        var renewed = false;
        foreach (var link in links)
            renewed |= await link.HandleUnauthorizedAsync(rejectedCredential, ct).ConfigureAwait(false);
        return renewed;
    }
}
