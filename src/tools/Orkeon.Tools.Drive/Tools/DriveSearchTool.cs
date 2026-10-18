using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Searches a drive.</summary>
[ToolContract("drive_search",
    Name = "drive_search",
    Description = "Search a cloud drive by words (names and content), name, MIME type, kind (file or folder), modification date or folder, inside the account's root folder; page with `cursor`. OneDrive needs `text` or `name_contains`. Needs the Read right.",
    Category = "Drive")]
internal sealed class DriveSearchTool : ToolBase<DriveSearchRequest, DriveSearchResponse>
{
    private readonly DriveAccess _access;

    /// <summary>Creates the tool.</summary>
    public DriveSearchTool(DriveAccess access, ILogger<DriveSearchTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        _access = access;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveSearchRequest request)
    {
        if (request is null)
            return "a request is required";
        if (string.IsNullOrWhiteSpace(request.Text) && string.IsNullOrWhiteSpace(request.NameContains) && string.IsNullOrWhiteSpace(request.MimeType)
            && string.IsNullOrWhiteSpace(request.Kind) && string.IsNullOrWhiteSpace(request.ModifiedAfter) && string.IsNullOrWhiteSpace(request.RawQuery))
        {
            return "give at least one criterion: `text`, `name_contains`, `mime_type`, `kind`, `modified_after` or `raw_query` (drive_list browses a folder)";
        }

        return request.Kind is { Length: > 0 } kind && !kind.Trim().Equals("file", StringComparison.OrdinalIgnoreCase) && !kind.Trim().Equals("folder", StringComparison.OrdinalIgnoreCase)
            ? "`kind` is file or folder"
            : null;
    }

    /// <inheritdoc />
    protected override async Task<DriveSearchResponse> ExecuteTypedAsync(DriveSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Read);
        var drive = _access.Drive(account);
        string? folderId = null;
        if (!string.IsNullOrWhiteSpace(request.FolderId))
            folderId = (await DriveAccess.FolderAsync(drive, request.FolderId, cancellationToken).ConfigureAwait(false)).Id;

        var query = new DriveSearchQuery
        {
            Text = request.Text,
            NameContains = request.NameContains,
            MimeType = request.MimeType,
            Folders = request.Kind is { Length: > 0 } kind ? kind.Trim().Equals("folder", StringComparison.OrdinalIgnoreCase) : null,
            ModifiedAfter = DriveToolHelpers.ParseDate(request.ModifiedAfter, "modified_after"),
            FolderId = folderId,
            RawQuery = request.RawQuery,
        };

        var (_, response) = await DriveToolHelpers.FitPageAsync(
            DriveToolHelpers.Limit(request.Limit),
            async limit =>
            {
                var page = await drive.SearchAsync(query, limit, request.Cursor, cancellationToken).ConfigureAwait(false);
                // Google Drive cannot bound a search to a subtree: what lies outside the root is dropped here.
                var kept = await DriveAccess.WithinRootAsync(drive, page.Items, cancellationToken).ConfigureAwait(false);
                return page with { Items = kept };
            },
            page => new DriveSearchResponse
            {
                Account = account.Name,
                Count = page.Items.Count,
                NextCursor = page.Cursor,
                Items = page.Items.Select(item => DriveToolHelpers.ToDto(item)).ToList(),
            },
            DriveToolHelpers.ResultBudget(Name)).ConfigureAwait(false);
        return response;
    }
}
