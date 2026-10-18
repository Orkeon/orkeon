using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Creates a folder.</summary>
[ToolContract("drive_create_folder",
    Name = "drive_create_folder",
    Description = "Create a folder in a cloud drive folder (the account's root folder by default); a name already taken there is refused. Needs the Write right.",
    Category = "Drive")]
internal sealed class DriveCreateFolderTool : ToolBase<DriveCreateFolderRequest, DriveCreateFolderResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveCreateFolderTool(DriveAccess access, ILogger<DriveCreateFolderTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveCreateFolderRequest request) =>
        DriveToolHelpers.CheckName(request?.Name, "name");

    /// <inheritdoc />
    protected override async Task<DriveCreateFolderResponse> ExecuteTypedAsync(DriveCreateFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Write);
        var drive = _access.Drive(account);
        var parent = await DriveAccess.FolderAsync(drive, request.ParentId, cancellationToken).ConfigureAwait(false);
        var folder = await drive.CreateFolderAsync(parent.Id, request.Name.Trim(), cancellationToken).ConfigureAwait(false);
        return new DriveCreateFolderResponse { Account = account.Name, Folder = DriveToolHelpers.ToDto(folder, withUrl: true) };
    }
}
