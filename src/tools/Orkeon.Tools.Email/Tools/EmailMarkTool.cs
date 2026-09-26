using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Sets or clears the read and flagged marks.</summary>
[ToolContract("email_mark",
    Name = "email_mark",
    Description = "Mark messages read or unread (`seen`) and flag or unflag them (`flagged`, a star on Gmail). Needs the Organize right.",
    Category = "Email")]
internal sealed class EmailMarkTool : ToolBase<EmailMarkRequest, EmailMarkResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailMarkTool(EmailAccess access, ILogger<EmailMarkTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailMarkRequest request) =>
        request?.Seen is null && request?.Flagged is null ? "pass `seen`, `flagged` or both" : null;

    /// <inheritdoc />
    protected override async Task<EmailMarkResponse> ExecuteTypedAsync(EmailMarkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = EmailToolHelpers.RequireIds(request.Ids);
        var account = _access.Authorize(request.Account, EmailRights.Organize);
        var updated = await _access.Mailbox(account, MailboxCapabilities.Flags)
            .SetFlagsAsync(ids, request.Seen, request.Flagged, cancellationToken).ConfigureAwait(false);
        return new EmailMarkResponse { Account = account.Name, Updated = updated };
    }
}
