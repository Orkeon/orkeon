using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Creates a folder (a label on Gmail).</summary>
[ToolContract("email_create_folder",
    Name = "email_create_folder",
    Description = "Create a folder in an e-mail account (a label on Gmail); '/' separates levels and missing parents are created. Answers created=false when it already exists. Needs the Organize right.",
    Category = "Email")]
internal sealed class EmailCreateFolderTool : ToolBase<EmailCreateFolderRequest, EmailCreateFolderResponse>
{
    private readonly EmailAccess _access;

    /// <summary>Creates the tool.</summary>
    public EmailCreateFolderTool(EmailAccess access, ILogger<EmailCreateFolderTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailCreateFolderRequest request) =>
        string.IsNullOrWhiteSpace(request?.Path) ? "`path` is required, e.g. Clients/ACME" : null;

    /// <inheritdoc />
    protected override async Task<EmailCreateFolderResponse> ExecuteTypedAsync(EmailCreateFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailRights.Organize);
        var (folder, created) = await _access.Mailbox(account, MailboxCapabilities.Folders)
            .CreateFolderAsync(request.Path, cancellationToken).ConfigureAwait(false);
        return new EmailCreateFolderResponse { Account = account.Name, Created = created, Folder = EmailToolHelpers.ToDto(folder) };
    }
}
