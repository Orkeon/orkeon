using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Moves messages to the trash, or deletes them for good.</summary>
[ToolContract("email_delete",
    Name = "email_delete",
    Description = "Delete messages: moved to the trash by default (needs the Delete right); `permanent: true` deletes them for good (needs the Purge right, cannot be undone). Returns each deleted id, with its new id in the trash.",
    Category = "Email")]
internal sealed class EmailDeleteTool : ToolBase<EmailDeleteRequest, EmailDeleteResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailDeleteTool(EmailAccess access, ILogger<EmailDeleteTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override async Task<EmailDeleteResponse> ExecuteTypedAsync(EmailDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = EmailToolHelpers.RequireIds(request.Ids);
        var permanent = request.Permanent ?? false;
        var account = _access.Authorize(request.Account, permanent ? EmailRights.Purge : EmailRights.Delete);
        var mailbox = _access.Mailbox(account, permanent ? MailboxCapabilities.None : MailboxCapabilities.Trash);
        var outcome = await mailbox.DeleteAsync(ids, permanent, cancellationToken).ConfigureAwait(false);
        return new EmailDeleteResponse
        {
            Account = account.Name,
            Deleted = outcome.Count,
            Permanent = outcome.Permanent,
            MovedTo = outcome.MovedTo,
            Messages = outcome.Messages.Select(item => new DeletedMessageDto { Id = item.Id, NewId = item.NewId }).ToList(),
        };
    }
}
