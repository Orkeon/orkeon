using System.Globalization;
using Microsoft.Extensions.Logging;
using MimeKit;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Sends a message, to the account's allowed recipients only.</summary>
[ToolContract("email_send",
    Name = "email_send",
    Description = "Send an e-mail from an account: a new message, a reply (reply_to_id, reply_all) or a forward (forward_id), with attachments from virtual paths. Only recipients the operator allowed for the account can receive it; prefer email_draft when a human should review. Needs the Send right.",
    Category = "Email")]
internal sealed partial class EmailSendTool : ToolBase<EmailComposeRequest, EmailSendResponse>
{
    private readonly EmailAccess _access;
    private readonly IFileSystemService _fileSystem;
    private readonly SendQuota _quota;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <summary>Creates the tool.</summary>
    public EmailSendTool(
        EmailAccess access, IFileSystemService fileSystem, SendQuota quota, TimeProvider time, ILogger<EmailSendTool>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(time);
        _access = access;
        _fileSystem = fileSystem;
        _quota = quota;
        _time = time;
        _log = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override async Task<EmailSendResponse> ExecuteTypedAsync(EmailComposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailComposeHelpers.RightsFor(request, EmailRights.Send));
        var mailbox = _access.Mailbox(account);
        var sender = _access.Sender(account);

        using var message = await EmailComposeHelpers.ComposeAsync(request, account, mailbox, _fileSystem, cancellationToken).ConfigureAwait(false);
        var recipients = EmailComposeHelpers.Recipients(message);
        CheckRecipients(account, recipients);
        if (!_quota.TryConsume(account.Name, account.Send.MaxPerHour))
        {
            throw new EmailToolException(
                EmailErrorCode.QuotaExceeded,
                string.Create(CultureInfo.InvariantCulture,
                    $"E-mail account '{account.Name}' already sent {account.Send.MaxPerHour} messages in the last hour (Send:MaxPerHour); nothing was sent."));
        }

        var from = new MailboxAddress(account.DisplayName, account.Address);
        await sender.SendAsync(message, from, recipients, cancellationToken).ConfigureAwait(false);
        var sentAt = _time.GetUtcNow();
        LogSent(_log, account.Name, recipients.Count, message.MessageId ?? string.Empty);

        string? warning = null;
        if (account.SaveSentCopy)
        {
            try
            {
                await mailbox.AppendToSentAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (EmailToolException ex)
            {
                // The message is gone already: failing the call now would invite a second send.
                warning = $"Sent, but filing a copy in the Sent folder failed: {ex.Message}";
            }
        }

        return new EmailSendResponse
        {
            Account = account.Name,
            MessageId = message.MessageId ?? string.Empty,
            Recipients = recipients.Select(address => address.Address).ToList(),
            SentAt = sentAt.ToString("O", CultureInfo.InvariantCulture),
            Warning = warning,
        };
    }

    private static void CheckRecipients(ResolvedEmailAccount account, IReadOnlyList<MailboxAddress> recipients)
    {
        if (account.Send.AllowedRecipients.Count == 0)
        {
            throw new EmailToolException(
                EmailErrorCode.RecipientNotAllowed,
                $"E-mail account '{account.Name}' allows no recipient yet: an operator lists them under Send:AllowedRecipients (an address, *@domain, or *). Nothing was sent; email_draft needs no allow-list.");
        }

        var refused = RecipientPolicy.Disallowed(recipients, account.Send.AllowedRecipients);
        if (refused.Count > 0)
        {
            throw new EmailToolException(
                EmailErrorCode.RecipientNotAllowed,
                $"E-mail account '{account.Name}' may not send to {string.Join(", ", refused.Select(address => address.Address))} (outside Send:AllowedRecipients). Nothing was sent.");
        }

        if (account.Send.MaxRecipients is { } max && recipients.Count > max)
        {
            throw new EmailToolException(
                EmailErrorCode.RecipientNotAllowed,
                string.Create(CultureInfo.InvariantCulture,
                    $"The message has {recipients.Count} recipients; account '{account.Name}' allows at most {max} (Send:MaxRecipients). Nothing was sent."));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "email_send: account {Account} sent a message to {RecipientCount} recipient(s), Message-Id {MessageId}")]
    private static partial void LogSent(ILogger logger, string account, int recipientCount, string messageId);
}
