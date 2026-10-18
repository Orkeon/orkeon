using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Lists the items of a folder.</summary>
[ToolContract("drive_list",
    Name = "drive_list",
    Description = "List the files and folders of a cloud drive folder (the account's root folder by default), folders first, with their ids, types, sizes and dates; page with `cursor`. Needs the Read right.",
    Category = "Drive")]
internal sealed class DriveListTool : ToolBase<DriveListRequest, DriveListResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveListTool(DriveAccess access, ILogger<DriveListTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<DriveListResponse> ExecuteTypedAsync(DriveListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Read);
        var drive = _access.Drive(account);
        var folder = await DriveAccess.FolderAsync(drive, request.FolderId, cancellationToken).ConfigureAwait(false);
        var folderDto = DriveToolHelpers.ToDto(folder);

        var (_, response) = await DriveToolHelpers.FitPageAsync(
            DriveToolHelpers.Limit(request.Limit),
            async limit =>
            {
                var page = await drive.ListAsync(folder.Id, limit, request.Cursor, cancellationToken).ConfigureAwait(false);
                // A cursor is the agent's to pass back, and on OneDrive it is an address: one that
                // lists another folder (outside the root) must not reach the agent. Only the
                // children of the folder checked above are kept.
                return page with { Items = page.Items.Where(item => item.ParentId == folder.Id).ToList() };
            },
            page => new DriveListResponse
            {
                Account = account.Name,
                Folder = folderDto,
                Count = page.Items.Count,
                NextCursor = page.Cursor,
                Items = page.Items.Select(item => DriveToolHelpers.ToDto(item)).ToList(),
            },
            DriveToolHelpers.ResultBudget(Name)).ConfigureAwait(false);
        return response;
    }
}
