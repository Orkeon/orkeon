using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Administration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Lists the configured drive accounts, their rights, their root folder and whether each is ready.</summary>
[ToolContract("drive_accounts",
    Name = "drive_accounts",
    Description = "List the cloud drive accounts (Google Drive, OneDrive) configured for this run: the name to pass as `account`, what each may do (rights: Read, Write, Organize, Share, Delete, Purge), the folder it is confined to and whether it is ready.",
    Category = "Drive")]
internal sealed class DriveAccountsTool : ToolBase<DriveAccountsRequest, DriveAccountsResponse>
{
    private readonly DriveAccountAdministration _administration;

    /// <summary>Creates the tool.</summary>
    public DriveAccountsTool(DriveAccountAdministration administration, ILogger<DriveAccountsTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(administration);
        _administration = administration;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override async Task<DriveAccountsResponse> ExecuteTypedAsync(DriveAccountsRequest request, CancellationToken cancellationToken)
    {
        var statuses = await _administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return new DriveAccountsResponse
        {
            Accounts = statuses.Select(status => new DriveAccountDto
            {
                Name = status.Name,
                Provider = status.Provider,
                Rights = status.Rights,
                Root = status.Root,
                Default = status.IsDefault,
                Ready = status.Ready,
                Problem = status.Problem,
            }).ToList(),
        };
    }
}
