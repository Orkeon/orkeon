using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;
using Orkeon.Tools.Drive.Security;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Shares an item with the allowed recipients, or by a public link when allowed.</summary>
[ToolContract("drive_share",
    Name = "drive_share",
    Description = "Share a cloud drive file or folder with e-mail addresses (as reader or writer), each on the account's share allow-list, or create a link anyone can open when the account allows public links. Needs the Share right.",
    Category = "Drive")]
internal sealed class DriveShareTool : ToolBase<DriveShareRequestDto, DriveShareResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveShareTool(DriveAccess access, ILogger<DriveShareTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveShareRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request?.Id))
            return "`id` is required: pass an id from drive_list or drive_search";
        var hasRecipients = request.Recipients?.Any(recipient => !string.IsNullOrWhiteSpace(recipient)) == true;
        if (request.PublicLink == true && hasRecipients)
            return "pass `recipients` or `public_link`, not both";
        if (request.PublicLink != true && !hasRecipients)
            return "`recipients` is required (or `public_link: true`)";
        return request.Role is { Length: > 0 } role && !role.Trim().Equals("reader", StringComparison.OrdinalIgnoreCase) && !role.Trim().Equals("writer", StringComparison.OrdinalIgnoreCase)
            ? "`role` is reader or writer"
            : null;
    }

    /// <inheritdoc />
    protected override async Task<DriveShareResponse> ExecuteTypedAsync(DriveShareRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Share);
        var publicLink = request.PublicLink ?? false;
        var recipients = (request.Recipients ?? []).Where(recipient => !string.IsNullOrWhiteSpace(recipient)).Select(recipient => recipient.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Authorize(account, recipients, publicLink);

        var drive = _access.Drive(account);
        var item = await DriveAccess.ItemAsync(drive, request.Id, cancellationToken).ConfigureAwait(false);
        var canEdit = request.Role?.Trim().Equals("writer", StringComparison.OrdinalIgnoreCase) == true;
        var result = await drive.ShareAsync(item, new DriveShareRequest
        {
            Recipients = recipients,
            CanEdit = canEdit,
            PublicLink = publicLink,
            Notify = request.Notify ?? false,
            Message = request.Message,
        }, cancellationToken).ConfigureAwait(false);

        return new DriveShareResponse
        {
            Account = account.Name,
            Id = item.Id,
            Role = canEdit ? "writer" : "reader",
            Recipients = result.Recipients,
            Link = result.Link,
        };
    }

    /// <summary>Refuses a share the account's policy does not allow, before anything is sent.</summary>
    private static void Authorize(ResolvedDriveAccount account, IReadOnlyList<string> recipients, bool publicLink)
    {
        if (publicLink)
        {
            if (!account.Share.AllowPublicLinks)
            {
                throw new DriveToolException(
                    DriveErrorCode.RecipientNotAllowed,
                    $"Drive account '{account.Name}' does not allow public links. An operator allows them with {Constants.DriveDefaults.SectionName}:Accounts:{account.Name}:Share:AllowPublicLinks.");
            }

            return;
        }

        var invalid = recipients.Where(recipient => !RecipientPatterns.IsAddress(recipient)).ToList();
        if (invalid.Count > 0)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"Not an e-mail address: {string.Join(", ", invalid)}.");

        var refused = recipients.Where(recipient => !RecipientPatterns.IsAllowed(recipient, account.Share.AllowedRecipients)).ToList();
        if (refused.Count > 0)
        {
            throw new DriveToolException(
                DriveErrorCode.RecipientNotAllowed,
                $"Drive account '{account.Name}' may not share with {string.Join(", ", refused)}. An operator adds addresses or '*@domain' to {Constants.DriveDefaults.SectionName}:Accounts:{account.Name}:Share:AllowedRecipients.");
        }
    }
}
