using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Moves messages to a folder.</summary>
[ToolContract("email_move",
    Name = "email_move",
    Description = "Move messages (ids from email_search) to a folder path or role (archive, junk, inbox…). Returns each message's new id. Needs the Organize right.",
    Category = "Email")]
internal sealed class EmailMoveTool : ToolBase<EmailMoveRequest, EmailMoveResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailMoveTool(EmailAccess access, ILogger<EmailMoveTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailMoveRequest request) =>
        string.IsNullOrWhiteSpace(request?.Destination) ? "`destination` is required: a folder path or role" : null;

    /// <inheritdoc />
    protected override async Task<EmailMoveResponse> ExecuteTypedAsync(EmailMoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = EmailToolHelpers.RequireIds(request.Ids);
        var account = _access.Authorize(request.Account, EmailRights.Organize);
        var moved = await _access.Mailbox(account, MailboxCapabilities.Move)
            .MoveAsync(ids, request.Destination.Trim(), cancellationToken).ConfigureAwait(false);
        return new EmailMoveResponse
        {
            Account = account.Name,
            Destination = request.Destination.Trim(),
            Moved = moved.Select(item => new MovedMessageDto { Id = item.Id, NewId = item.NewId }).ToList(),
        };
    }
}
