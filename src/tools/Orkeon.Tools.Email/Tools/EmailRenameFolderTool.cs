using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Renames a folder.</summary>
[ToolContract("email_rename_folder",
    Name = "email_rename_folder",
    Description = "Rename a folder of an e-mail account (its last segment); system folders (inbox, sent, drafts, trash…) are refused. Needs the Organize right.",
    Category = "Email")]
internal sealed class EmailRenameFolderTool : ToolBase<EmailRenameFolderRequest, EmailRenameFolderResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailRenameFolderTool(EmailAccess access, ILogger<EmailRenameFolderTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailRenameFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Path))
            return "`path` is required";
        return string.IsNullOrWhiteSpace(request.NewName) ? "`new_name` is required" : null;
    }

    /// <inheritdoc />
    protected override async Task<EmailRenameFolderResponse> ExecuteTypedAsync(EmailRenameFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailRights.Organize);
        var folder = await _access.Mailbox(account, MailboxCapabilities.Folders)
            .RenameFolderAsync(request.Path, request.NewName, cancellationToken).ConfigureAwait(false);
        return new EmailRenameFolderResponse { Account = account.Name, PreviousPath = request.Path.Trim().Trim('/'), Folder = EmailToolHelpers.ToDto(folder) };
    }
}
