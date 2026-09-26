namespace Orkeon.Tools.Email.Auth;

/// <summary>
/// The token store of a host that provides none. Password accounts never touch it; an OAuth
/// account gets a message that says where tokens come from instead of a silent failure.
/// </summary>
internal sealed class UnavailableEmailTokenStore : IEmailTokenStore
{
    private const string Message =
        "This host keeps no OAuth tokens for e-mail accounts. Use a password account here, or `orkeon run`, " +
        "which keeps them once an OAuth2 account is declared and its token directory can be created (it warns " +
        "at start when it cannot); a host of its own registers an IEmailTokenStore.";

    /// <inheritdoc />
    public Task<EmailTokenSet?> ReadAsync(string key, CancellationToken cancellationToken) =>
        throw new EmailToolException(EmailErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task WriteAsync(string key, EmailTokenSet tokens, CancellationToken cancellationToken) =>
        throw new EmailToolException(EmailErrorCode.NotConfigured, Message);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) =>
        throw new EmailToolException(EmailErrorCode.NotConfigured, Message);
}
