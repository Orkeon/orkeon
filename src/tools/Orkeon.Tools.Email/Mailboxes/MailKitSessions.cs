using System.Net.Sockets;
using MailKit;
using MailKit.Security;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>
/// Opens the transport of a MailKit client. The network implementation dials the configured
/// server; the tests hand MailKit a scripted in-memory stream through the same seam.
/// </summary>
internal interface IMailServiceConnector
{
    /// <summary>Connects <paramref name="client"/> to <paramref name="endpoint"/>.</summary>
    Task ConnectAsync(IMailService client, MailEndpoint endpoint, CancellationToken cancellationToken);
}

/// <summary>Dials the configured server with the configured transport security.</summary>
internal sealed class NetworkMailServiceConnector : IMailServiceConnector
{
    /// <inheritdoc />
    public Task ConnectAsync(IMailService client, MailEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);
        // No catch-all arm: a value outside the three would otherwise dial in clear text.
        var options = endpoint.Security switch
        {
            TransportSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            TransportSecurity.StartTls => SecureSocketOptions.StartTls,
            TransportSecurity.None => SecureSocketOptions.None,
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Security, "Unknown transport security."),
        };
        return client.ConnectAsync(endpoint.Host, endpoint.Port, options, cancellationToken);
    }
}

/// <summary>Connection, authentication and failure translation shared by the MailKit backends.</summary>
internal static class MailKitSessions
{
    /// <summary>Connects and authenticates <paramref name="client"/> for <paramref name="account"/>.</summary>
    public static async Task OpenAsync(
        IMailService client,
        MailEndpoint endpoint,
        ResolvedEmailAccount account,
        IMailServiceConnector connector,
        EmailCredentialProvider credentials,
        CancellationToken cancellationToken)
    {
        if (account.Timeout is { } timeout)
            client.Timeout = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);

        var credential = await credentials.GetAsync(account, cancellationToken).ConfigureAwait(false);
        try
        {
            await connector.ConnectAsync(client, endpoint, cancellationToken).ConfigureAwait(false);
            try
            {
                await AuthenticateAsync(client, credential, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException) when (credential is BearerCredential)
            {
                // A token refused once may have been revoked, or replaced by a new sign-in: the
                // store is read again and the token refreshed, and the server is asked once more.
                credential = await credentials.GetAsync(account, cancellationToken, refused: credential).ConfigureAwait(false);
                if (!client.IsConnected)
                    await connector.ConnectAsync(client, endpoint, cancellationToken).ConfigureAwait(false);
                await AuthenticateAsync(client, credential, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (Translate(ex, account, endpoint) is { } translated)
        {
            throw translated;
        }
    }

    private static Task AuthenticateAsync(IMailService client, EmailCredential credential, CancellationToken cancellationToken) => credential switch
    {
        BearerCredential bearer => client.AuthenticateAsync(new SaslMechanismOAuth2(bearer.Username, bearer.AccessToken), cancellationToken),
        PasswordCredential password => client.AuthenticateAsync(password.Username, password.Password, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(credential), credential.GetType().Name, "Unknown credential."),
    };

    /// <summary>
    /// The actionable form of a MailKit or socket failure, or null for anything else (a
    /// cancellation, an <see cref="EmailToolException"/> already written for the agent).
    /// </summary>
    public static EmailToolException? Translate(Exception exception, ResolvedEmailAccount account, MailEndpoint? endpoint)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(account);
        var server = endpoint is null ? "the mail server" : $"{endpoint.Host}:{endpoint.Port}";
        return exception switch
        {
            EmailToolException or OperationCanceledException => null,
            AuthenticationException => new EmailToolException(
                EmailErrorCode.AuthenticationFailed,
                $"{server} refused the credentials of e-mail account '{account.Name}'. {AuthenticationHint(account)}{InUseHint(account, endpoint)} ({exception.Message})",
                exception),
            NotSupportedException when exception.Message.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase) => new EmailToolException(
                EmailErrorCode.ServerError,
                $"{server} does not offer STARTTLS, which Security StartTls requires: nothing was sent unencrypted. Check the port and Security (SslOnConnect for 993/995/465).",
                exception),
            SslHandshakeException => new EmailToolException(
                EmailErrorCode.ServerError,
                $"The TLS handshake with {server} failed: check the host, the port and Security (SslOnConnect for 993/995/465, StartTls for 143/110/587).",
                exception),
            FolderNotFoundException folder => new EmailToolException(
                EmailErrorCode.FolderNotFound,
                $"The folder '{folder.FolderName}' does not exist in account '{account.Name}': list them with email_folders.",
                exception),
            MessageNotFoundException => new EmailToolException(
                EmailErrorCode.MessageNotFound,
                $"The message no longer exists in account '{account.Name}' (moved or deleted): search again.",
                exception),
            CommandException => new EmailToolException(
                EmailErrorCode.ServerError,
                $"{server} refused the command: {exception.Message}",
                exception),
            ProtocolException or ServiceNotConnectedException or ServiceNotAuthenticatedException or IOException or SocketException
                or TimeoutException => new EmailToolException(
                EmailErrorCode.ServerError,
                $"The connection to {server} failed: {exception.Message}",
                exception),
            _ => null,
        };
    }

    // MailKit reports a POP3 maildrop another session holds ([IN-USE]) as a plain
    // authentication failure: the message says it may be that.
    private static string InUseHint(ResolvedEmailAccount account, MailEndpoint? endpoint) =>
        account.Incoming == IncomingProtocol.Pop3 && endpoint == account.IncomingEndpoint
            ? " A POP3 mailbox also refuses a session while another mail client holds it: if the credentials are right, retry in a moment."
            : string.Empty;

    private static string AuthenticationHint(ResolvedEmailAccount account) => account.Provider switch
    {
        EmailProvider.Gmail when account.Auth.Method == EmailAuthMethod.Password =>
            "Gmail needs an app password (Google account > Security > 2-Step Verification > App passwords), not the account password.",
        _ when account.Auth.Method == EmailAuthMethod.OAuth2 =>
            $"The OAuth token was refused: run `orkeon email login {account.Name}` again.",
        _ => "Check the user name and the password variable.",
    };
}
