using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Saves a message in the Drafts folder without sending it.</summary>
[ToolContract("email_draft",
    Name = "email_draft",
    Description = "Write an e-mail and save it in the account's Drafts folder WITHOUT sending it, for a human to review and send: a new message, a reply (reply_to_id, reply_all) or a forward (forward_id), with attachments from virtual paths. Needs the Draft right.",
    Category = "Email")]
internal sealed class EmailDraftTool : ToolBase<EmailComposeRequest, EmailDraftResponse>
{
    private readonly EmailAccess _access;
    private readonly IFileSystemService _fileSystem;

    /// <summary>Creates the tool.</summary>
    public EmailDraftTool(EmailAccess access, IFileSystemService fileSystem, ILogger<EmailDraftTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _access = access;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override async Task<EmailDraftResponse> ExecuteTypedAsync(EmailComposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailComposeHelpers.RightsFor(request, EmailRights.Draft));
        var mailbox = _access.Mailbox(account, MailboxCapabilities.Drafts);

        using var message = await EmailComposeHelpers.ComposeAsync(request, account, mailbox, _fileSystem, cancellationToken).ConfigureAwait(false);
        var (id, folder) = await mailbox.SaveDraftAsync(message, cancellationToken).ConfigureAwait(false);
        return new EmailDraftResponse
        {
            Account = account.Name,
            Id = id,
            Folder = folder,
            MessageId = message.MessageId ?? string.Empty,
            Recipients = EmailComposeHelpers.Recipients(message).Select(address => address.Address).ToList(),
        };
    }
}
