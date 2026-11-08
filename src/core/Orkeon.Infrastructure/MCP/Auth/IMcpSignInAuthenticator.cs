namespace Orkeon.Infrastructure.MCP.Auth;

/// <summary>
/// An authenticator that may need a person: it can sign in before a request is sent, so that the wait for the
/// person is not counted in the request's own timeout (<c>ConnectTimeout</c>, <c>CallTimeout</c>), and it can
/// apply what it already holds without ever starting a sign-in (the closing <c>DELETE</c> of a session).
/// </summary>
internal interface IMcpSignInAuthenticator : IMcpRequestAuthenticator
{
    /// <summary>
    /// Makes sure a usable credential is held, signing in when it takes to (the device flow, bounded by the
    /// lifetime of the device code). Cheap when the credential in cache is still good.
    /// </summary>
    /// <param name="ct">Cancelled by the caller; never by a request's timeout.</param>
    ValueTask EnsureSignedInAsync(CancellationToken ct = default);

    /// <summary>Adds the credential already held, if any: never renews it, never signs in.</summary>
    /// <param name="request">The request about to be sent.</param>
    /// <param name="ct">The cancellation token.</param>
    ValueTask ApplyWithoutSignInAsync(HttpRequestMessage request, CancellationToken ct = default);
}
