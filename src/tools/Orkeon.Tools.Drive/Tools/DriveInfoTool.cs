using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Reads the metadata of one item.</summary>
[ToolContract("drive_info",
    Name = "drive_info",
    Description = "Read the metadata of one file or folder of a cloud drive: name, type, MIME type, size, date, parent and browser address. Needs the Read right.",
    Category = "Drive")]
internal sealed class DriveInfoTool : ToolBase<DriveInfoRequest, DriveInfoResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveInfoTool(DriveAccess access, ILogger<DriveInfoTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveInfoRequest request) =>
        string.IsNullOrWhiteSpace(request?.Id) ? "`id` is required: pass an id from drive_list or drive_search, or `root`" : null;

    /// <inheritdoc />
    protected override async Task<DriveInfoResponse> ExecuteTypedAsync(DriveInfoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Read);
        var item = await DriveAccess.ItemAsync(_access.Drive(account), request.Id, cancellationToken).ConfigureAwait(false);
        return new DriveInfoResponse { Account = account.Name, Item = DriveToolHelpers.ToDto(item, withUrl: true) };
    }
}
