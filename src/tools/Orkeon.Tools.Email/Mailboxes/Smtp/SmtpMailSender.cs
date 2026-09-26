using System.Globalization;
using MailKit.Net.Smtp;
using MimeKit;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Mailboxes.Smtp;

/// <summary>
/// Sends over SMTP submission, one session per message. The envelope is passed explicitly —
/// sender and the checked recipient list — so no header (<c>Resent-*</c> included) can widen it.
/// </summary>
internal sealed class SmtpMailSender : IMailSender
{
    /// <summary>
    /// The message as it goes on the wire: without the Bcc and Resent-Bcc headers, which would
    /// show every recipient who was copied blind. They stay in the message itself (the Sent copy).
    /// </summary>
    private static readonly FormatOptions WireFormat = CreateWireFormat();

    private readonly ResolvedEmailAccount _account;
    private readonly MailEndpoint _endpoint;
    private readonly IMailServiceConnector _connector;
    private readonly EmailCredentialProvider _credentials;

    /// <summary>Creates the sender of <paramref name="account"/>.</summary>
    public SmtpMailSender(ResolvedEmailAccount account, IMailServiceConnector connector, EmailCredentialProvider credentials)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _endpoint = account.OutgoingEndpoint
            ?? throw new ArgumentException("An SMTP account needs an outgoing endpoint.", nameof(account));
        _connector = connector;
        _credentials = credentials;
    }

    /// <inheritdoc />
    public async Task<SendReceipt> SendAsync(
        MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipients);

        using var client = new SmtpClient();
        try
        {
            await MailKitSessions.OpenAsync(client, _endpoint, _account, _connector, _credentials, cancellationToken).ConfigureAwait(false);
            if (client.MaxSize > 0)
            {
                var size = MessageSizes.Measure(message);
                if (size > client.MaxSize)
                {
                    throw new EmailToolException(
                        EmailErrorCode.TooLarge,
                        string.Create(CultureInfo.InvariantCulture,
                            $"The message is {size / 1024} KB; {_endpoint.Host} accepts at most {client.MaxSize / 1024} KB: attach fewer or smaller files."));
                }
            }

            var response = await client.SendAsync(WireFormat, message, sender, recipients, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return new SendReceipt(response);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted)
        {
            throw new EmailToolException(
                EmailErrorCode.ServerError,
                $"{_endpoint.Host} refused the recipient {ex.Mailbox?.Address}: {ex.Message}",
                ex);
        }
        catch (Exception ex) when (MailKitSessions.Translate(ex, _account, _endpoint) is { } translated)
        {
            throw translated;
        }
    }

    private static FormatOptions CreateWireFormat()
    {
        var format = FormatOptions.Default.Clone();
        format.HiddenHeaders.Add(HeaderId.Bcc);
        format.HiddenHeaders.Add(HeaderId.ResentBcc);
        return format;
    }
}
