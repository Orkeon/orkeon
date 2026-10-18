namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>
/// The interactive sign-in of an OAuth account: the device-code flow, or the loopback flow with
/// its paste fallback. Shared by the families' administration classes; it never stores anything,
/// the caller keeps the tokens its own way.
/// </summary>
internal static class OAuthSignIn
{
    /// <summary>Runs the flow <paramref name="settings"/> names and returns the tokens it obtained.</summary>
    /// <param name="oauth">The OAuth client.</param>
    /// <param name="settings">The account's OAuth settings.</param>
    /// <param name="clientSecret">The client secret, when the account declares one.</param>
    /// <param name="account">The account name, shown to the user.</param>
    /// <param name="loginHint">The address the provider may pre-fill, or null.</param>
    /// <param name="interaction">The terminal conversation.</param>
    /// <param name="cancellationToken">Cancels the sign-in.</param>
    public static Task<OAuthTokenSet> RunAsync(
        OAuth2Client oauth, OAuthSettings settings, string? clientSecret, string account, string? loginHint,
        IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(interaction);
        return settings.Flow == OAuthFlow.DeviceCode
            ? DeviceAsync(oauth, settings, clientSecret, account, interaction, cancellationToken)
            : LoopbackAsync(oauth, settings, clientSecret, account, loginHint, interaction, cancellationToken);
    }

    private static async Task<OAuthTokenSet> DeviceAsync(
        OAuth2Client oauth, OAuthSettings settings, string? secret, string account, IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        var grant = await oauth.RequestDeviceCodeAsync(settings, cancellationToken).ConfigureAwait(false);
        await interaction.ShowDeviceCodeAsync(account, grant.VerificationUri, grant.UserCode, grant.ExpiresIn, cancellationToken).ConfigureAwait(false);
        return await oauth.PollDeviceCodeAsync(settings, secret, grant, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OAuthTokenSet> LoopbackAsync(
        OAuth2Client oauth, OAuthSettings settings, string? secret, string account, string? loginHint,
        IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        using var listener = LoopbackRedirectListener.Start();
        var session = PkceSession.Create();
        var authorization = OAuth2Client.BuildAuthorizationUri(settings, listener.RedirectUri, session, loginHint);
        await interaction.ShowAuthorizationUrlAsync(account, authorization, cancellationToken).ConfigureAwait(false);

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var viaBrowser = listener.WaitAsync(session.State, race.Token);
        // The paste prompt gets a thread of its own: a terminal read blocks its caller until a
        // line comes, whatever its signature (Console.In does), and the browser's redirect must
        // still end the sign-in while nobody types anything.
        var viaPaste = Task.Run(
            () => WaitForPastedRedirectAsync(account, authorization, listener.RedirectUri, interaction, race.Token), CancellationToken.None);
        var first = await Task.WhenAny(viaBrowser, viaPaste).ConfigureAwait(false);
        var redirect = await first.ConfigureAwait(false);
        await race.CancelAsync().ConfigureAwait(false);

        // The state first: an address from an earlier attempt, or forged, is not this sign-in's
        // answer, whatever it says.
        if (!string.Equals(redirect.State, session.State, StringComparison.Ordinal))
            throw new OAuthException(OAuthFailure.AuthenticationFailed, "The redirect did not come from this sign-in (state mismatch); run the login again.");
        if (redirect.Error is { } error)
            throw new OAuthException(OAuthFailure.LoginRequired, $"The sign-in was refused: {error}.");
        if (redirect.Code is not { } code)
            throw new OAuthException(OAuthFailure.AuthenticationFailed, "The redirect carries no authorization code; run the login again.");

        return await oauth.ExchangeCodeAsync(settings, secret, code, listener.RedirectUri, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads pasted lines until one is an outcome of a sign-in. A line that is not is said to the
    /// interaction, with the reason, and the wait goes on: a rejected paste is not a failed
    /// sign-in. An empty line says nothing.
    /// </summary>
    private static async Task<AuthorizationRedirect> WaitForPastedRedirectAsync(
        string account, Uri authorization, Uri redirectUri, IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await interaction.ReadRedirectAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                // Nothing can be pasted here: only the browser redirect can finish the sign-in.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var pasted = Uri.TryCreate(line.Trim(), UriKind.Absolute, out var address) ? address : null;
            if (pasted is not null && AuthorizationRedirect.Parse(pasted) is { IsOutcome: true } redirect)
                return redirect;

            await interaction.ShowRedirectRejectedAsync(account, WhyRejected(pasted, authorization, redirectUri), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Why a pasted line is not the redirect of a sign-in, in one sentence that never repeats the
    /// line: it may hold an authorization code, a secret while it lives.
    /// </summary>
    private static string WhyRejected(Uri? pasted, Uri authorization, Uri redirectUri)
    {
        // A bare path is an absolute file address under Unix: only a web address is one here.
        if (pasted is null || (pasted.Scheme != Uri.UriSchemeHttp && pasted.Scheme != Uri.UriSchemeHttps))
            return "The pasted text is not an address.";
        if (string.Equals(pasted.Host, authorization.Host, StringComparison.OrdinalIgnoreCase))
            return "The pasted address is the one to open, not the one the browser ended on.";
        if (!pasted.IsLoopback)
            return $"The pasted address is not the redirect address of this sign-in ({redirectUri.AbsoluteUri}).";
        return "The pasted address carries no authorization code: it may be cut short.";
    }
}
