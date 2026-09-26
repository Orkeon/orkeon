namespace Orkeon.Tools.Email.Auth;

/// <summary>The OAuth tokens held for one e-mail account.</summary>
public sealed record EmailTokenSet
{
    /// <summary>The bearer token sent to the mail server or to Microsoft Graph.</summary>
    public required string AccessToken { get; init; }

    /// <summary>The long-lived token that obtains new access tokens, when the provider issued one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>When <see cref="AccessToken"/> stops being accepted.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The scopes the tokens were granted for.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
}

/// <summary>
/// Where the OAuth tokens of e-mail accounts are kept between runs. The runner host provides
/// one over an internal VFS root that no agent-facing tool resolves; a host of its own may
/// supply any implementation, a vault-backed one for instance.
/// </summary>
public interface IEmailTokenStore
{
    /// <summary>The tokens stored under <paramref name="key"/>, or null.</summary>
    Task<EmailTokenSet?> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="tokens"/> under <paramref name="key"/>, replacing what was there.</summary>
    Task WriteAsync(string key, EmailTokenSet tokens, CancellationToken cancellationToken);

    /// <summary>Forgets the tokens under <paramref name="key"/>; false when there were none.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);
}
