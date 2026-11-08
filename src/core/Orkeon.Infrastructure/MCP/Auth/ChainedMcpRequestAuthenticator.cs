namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>Applies several authenticators in order; disposing it disposes them.</summary>
internal sealed class ChainedMcpRequestAuthenticator(IReadOnlyList<IMcpRequestAuthenticator> links) : IMcpSignInAuthenticator, IDisposable
{
    public void Dispose()
    {
        foreach (var link in links)
            (link as IDisposable)?.Dispose();
    }

    public async ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        foreach (var link in links)
            await link.ApplyAsync(request, ct).ConfigureAwait(false);
    }

    public async ValueTask EnsureSignedInAsync(CancellationToken ct = default)
    {
        foreach (var link in links)
        {
            if (link is IMcpSignInAuthenticator signIn)
                await signIn.EnsureSignedInAsync(ct).ConfigureAwait(false);
        }
    }

    public async ValueTask ApplyWithoutSignInAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        foreach (var link in links)
        {
            if (link is IMcpSignInAuthenticator signIn)
                await signIn.ApplyWithoutSignInAsync(request, ct).ConfigureAwait(false);
            else
                await link.ApplyAsync(request, ct).ConfigureAwait(false);
        }
    }

    public async ValueTask<bool> HandleUnauthorizedAsync(string? rejectedCredential, CancellationToken ct = default)
    {
        var renewed = false;
        foreach (var link in links)
            renewed |= await link.HandleUnauthorizedAsync(rejectedCredential, ct).ConfigureAwait(false);
        return renewed;
    }
}
