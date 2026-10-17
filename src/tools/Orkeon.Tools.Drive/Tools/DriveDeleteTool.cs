using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Moves an item to the trash, or deletes it for good.</summary>
[ToolContract("drive_delete",
    Name = "drive_delete",
    Description = "Move a cloud drive file or folder to the trash (a person can restore it), or delete it for good with `permanent` (needs the Purge right). Needs the Delete right.",
    Category = "Drive")]
internal sealed class DriveDeleteTool : ToolBase<DriveDeleteRequest, DriveDeleteResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveDeleteTool(DriveAccess access, ILogger<DriveDeleteTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveDeleteRequest request) =>
        string.IsNullOrWhiteSpace(request?.Id) ? "`id` is required: pass an id from drive_list or drive_search" : null;

    /// <inheritdoc />
    protected override async Task<DriveDeleteResponse> ExecuteTypedAsync(DriveDeleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var permanent = request.Permanent ?? false;
        var account = _access.Authorize(request.Account, permanent ? DriveRights.Delete | DriveRights.Purge : DriveRights.Delete);
        var drive = _access.Drive(account);
        if (permanent && (drive.Capabilities & DriveCapabilities.PermanentDelete) == 0)
        {
            throw new DriveToolException(
                DriveErrorCode.Unsupported,
                $"Drive account '{account.Name}' is a personal OneDrive, which deletes nothing for good: delete without `permanent` (to the recycle bin).");
        }

        var root = await drive.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var item = await DriveAccess.ItemAsync(drive, request.Id, cancellationToken).ConfigureAwait(false);
        if (item.Id == root.Id)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, "The account's root folder cannot be deleted.");

        if (permanent)
            await drive.DeletePermanentlyAsync(item.Id, cancellationToken).ConfigureAwait(false);
        else
            await drive.TrashAsync(item.Id, cancellationToken).ConfigureAwait(false);
        return new DriveDeleteResponse { Account = account.Name, Id = item.Id, Name = item.Name, Permanent = permanent };
    }
}
