using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Lists the folders of an account.</summary>
[ToolContract("email_folders",
    Name = "email_folders",
    Description = "List the folders of an e-mail account with their role (inbox, sent, drafts, trash, junk, archive, all), message and unread counts. Needs the Read right.",
    Category = "Email")]
internal sealed class EmailFoldersTool : ToolBase<EmailFoldersRequest, EmailFoldersResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailFoldersTool(EmailAccess access, ILogger<EmailFoldersTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<EmailFoldersResponse> ExecuteTypedAsync(EmailFoldersRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailRights.Read);
        var folders = await _access.Mailbox(account).ListFoldersAsync(cancellationToken).ConfigureAwait(false);
        return new EmailFoldersResponse
        {
            Account = account.Name,
            Folders = folders.Select(EmailToolHelpers.ToDto).ToList(),
        };
    }
}
