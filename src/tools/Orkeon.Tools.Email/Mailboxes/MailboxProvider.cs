using System.Collections.Concurrent;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes.Graph;
using Orkeon.Tools.Email.Mailboxes.Imap;
using Orkeon.Tools.Email.Mailboxes.Pop3;
using Orkeon.Tools.Email.Mailboxes.Smtp;

namespace Orkeon.Tools.Email.Mailboxes;

/// <summary>
/// Builds each account's backend once and keeps it, so an IMAP account reuses its connection
/// across tool calls. Disposing the provider closes every pooled connection.
/// </summary>
internal sealed class MailboxProvider : IMailboxProvider, IAsyncDisposable, IDisposable
{
    private readonly IMailServiceConnector _connector;
    private readonly EmailCredentialProvider _credentials;
    private readonly Func<HttpClient> _httpClients;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Lazy<IMailbox>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the provider.</summary>
    /// <param name="connector">Opens the MailKit transports.</param>
    /// <param name="credentials">Supplies passwords and tokens.</param>
    /// <param name="httpClients">Hands out the HTTP client of the Graph backend.</param>
    /// <param name="time">Clock of the connection pools and caches.</param>
    public MailboxProvider(IMailServiceConnector connector, EmailCredentialProvider credentials, Func<HttpClient> httpClients, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(httpClients);
        ArgumentNullException.ThrowIfNull(time);
        _connector = connector;
        _credentials = credentials;
        _httpClients = httpClients;
        _time = time;
    }

    /// <inheritdoc />
    public IMailbox GetMailbox(ResolvedEmailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _mailboxes.GetOrAdd(account.Name, _ => new Lazy<IMailbox>(() => Create(account))).Value;
    }

    /// <inheritdoc />
    public IMailSender GetSender(ResolvedEmailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Outgoing switch
        {
            OutgoingProtocol.Smtp => new SmtpMailSender(account, _connector, _credentials),
            OutgoingProtocol.Graph => new GraphMailSender(new GraphClient(_httpClients(), account, _credentials)),
            _ => throw new EmailToolException(
                EmailErrorCode.Unsupported,
                $"E-mail account '{account.Name}' declares no outgoing server (Outgoing:Host), so it cannot send."),
        };
    }

    /// <summary>
    /// Closes the pooled connections without a goodbye, for a container disposed synchronously
    /// (a host that disposes asynchronously gets the orderly LOGOUT of <see cref="DisposeAsync"/>).
    /// </summary>
    public void Dispose()
    {
        foreach (var mailbox in _mailboxes.Values.Where(lazy => lazy.IsValueCreated).Select(lazy => lazy.Value))
            (mailbox as IDisposable)?.Dispose();
        _mailboxes.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var mailbox in _mailboxes.Values.Where(lazy => lazy.IsValueCreated).Select(lazy => lazy.Value))
        {
            if (mailbox is IAsyncDisposable disposable)
                await disposable.DisposeAsync().ConfigureAwait(false);
        }

        _mailboxes.Clear();
    }

    private IMailbox Create(ResolvedEmailAccount account) => account.Incoming switch
    {
        IncomingProtocol.Imap => new ImapMailbox(account, _connector, _credentials, _time),
        IncomingProtocol.Pop3 => new Pop3Mailbox(account, _connector, _credentials),
        _ => new GraphMailbox(new GraphClient(_httpClients(), account, _credentials), _time),
    };
}
