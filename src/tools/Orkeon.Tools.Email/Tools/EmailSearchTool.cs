using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Constants;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Searches one folder of an account, newest first, one page at a time.</summary>
[ToolContract("email_search",
    Name = "email_search",
    Description = "Search a folder of an e-mail account (default: inbox), newest first: unread/flagged, from, to, subject, text, dates, attachments, or a provider-native raw_query. Returns ids for email_read, email_move, email_mark, email_delete. Needs the Read right.",
    Category = "Email")]
internal sealed class EmailSearchTool : ToolBase<EmailSearchRequest, EmailSearchResponse>
{
    private readonly EmailAccess _access;
    private readonly EmailContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public EmailSearchTool(EmailAccess access, EmailContentScreen screen, ILogger<EmailSearchTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<EmailSearchResponse> ExecuteTypedAsync(EmailSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailRights.Read);
        var search = new MailSearch
        {
            Folder = string.IsNullOrWhiteSpace(request.Folder) ? FolderRoles.Inbox : request.Folder.Trim(),
            UnreadOnly = request.UnreadOnly ?? false,
            FlaggedOnly = request.FlaggedOnly ?? false,
            From = request.From,
            To = request.To,
            Subject = request.Subject,
            Text = request.Text,
            Since = EmailToolHelpers.ParseDate(request.Since, "since"),
            Before = EmailToolHelpers.ParseDate(request.Before, "before"),
            HasAttachments = request.HasAttachments,
            RawQuery = request.RawQuery,
            Limit = Math.Clamp(request.Limit ?? EmailDefaults.DefaultSearchLimit, 1, EmailDefaults.MaxSearchLimit),
            Cursor = request.Cursor,
        };

        var page = await _access.Mailbox(account).SearchAsync(search, cancellationToken).ConfigureAwait(false);
        return new EmailSearchResponse
        {
            Notice = EmailContentScreen.UntrustedNotice,
            Account = account.Name,
            Folder = search.Folder,
            Count = page.Messages.Count,
            NextCursor = page.NextCursor,
            Messages = page.Messages.Select(message => EmailToolHelpers.ToDto(message, _screen)).ToList(),
        };
    }
}
