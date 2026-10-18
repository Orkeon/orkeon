using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Moves and/or renames an item.</summary>
[ToolContract("drive_move",
    Name = "drive_move",
    Description = "Move a cloud drive file or folder into another folder (`destination_id`), rename it (`new_name`), or both, inside the account's root folder. Needs the Organize right.",
    Category = "Drive")]
internal sealed class DriveMoveTool : ToolBase<DriveMoveRequest, DriveMoveResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveMoveTool(DriveAccess access, ILogger<DriveMoveTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveMoveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Id))
            return "`id` is required: pass an id from drive_list or drive_search";
        if (string.IsNullOrWhiteSpace(request.DestinationId) && string.IsNullOrWhiteSpace(request.NewName))
            return "pass `destination_id` to move, `new_name` to rename, or both";
        return request.NewName is null ? null : DriveToolHelpers.CheckName(request.NewName, "new_name");
    }

    /// <inheritdoc />
    protected override async Task<DriveMoveResponse> ExecuteTypedAsync(DriveMoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Organize);
        var drive = _access.Drive(account);
        var root = await drive.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var item = await DriveAccess.ItemAsync(drive, request.Id, cancellationToken).ConfigureAwait(false);
        if (item.Id == root.Id)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, "The account's root folder cannot be moved or renamed.");

        string? destination = null;
        if (!string.IsNullOrWhiteSpace(request.DestinationId))
        {
            var folder = await DriveAccess.FolderAsync(drive, request.DestinationId, cancellationToken).ConfigureAwait(false);
            if (folder.Id == item.Id || (item.IsFolder && await drive.IsWithinAsync(folder, item, cancellationToken).ConfigureAwait(false)))
                throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' cannot be moved into itself or into one of its own folders.");
            destination = folder.Id;
        }

        var moved = await drive.MoveAsync(item, destination, string.IsNullOrWhiteSpace(request.NewName) ? null : request.NewName.Trim(), cancellationToken).ConfigureAwait(false);
        return new DriveMoveResponse { Account = account.Name, Item = DriveToolHelpers.ToDto(moved, withUrl: true) };
    }
}
