using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Searches a drive.</summary>
[ToolContract("drive_search",
    Name = "drive_search",
    Description = "Search a cloud drive by words (names and content), name, MIME type, kind (file or folder), modification date or folder, inside the account's root folder; page with `cursor`. OneDrive needs `text` or `name_contains`. Needs the Read right.",
    Category = "Drive")]
internal sealed partial class DriveSearchTool : ToolBase<DriveSearchRequest, DriveSearchResponse>
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
            limit => FillAsync(drive, account, query, limit, request.Cursor, cancellationToken),
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

    /// <summary>
    /// A page of up to <paramref name="limit"/> items inside the account's root. Neither provider
    /// bounds a search to the root's subtree (Google cannot, OneDrive personal may not), so the
    /// first provider pages can hold nothing but items from elsewhere in the drive: provider pages
    /// are read on, each asked for what is still missing so nothing kept is cut, up to
    /// <see cref="DriveDefaults.MaxSearchPages"/>. The cursor resumes after the last page read.
    /// </summary>
    private async Task<DrivePage> FillAsync(IDrive drive, ResolvedDriveAccount account, DriveSearchQuery query, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var kept = new List<DriveItem>(limit);
        for (var pages = 0; pages < DriveDefaults.MaxSearchPages; pages++)
        {
            var page = await drive.SearchAsync(query, limit - kept.Count, cursor, cancellationToken).ConfigureAwait(false);
            var within = await DriveAccess.WithinRootAsync(drive, page.Items, cancellationToken).ConfigureAwait(false);
            if (within.Count < page.Items.Count && _logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var item in page.Items.Except(within))
                    LogDropped(_logger, account.Name, item.Id, item.ParentId, item.ParentPath, item.DriveId);
            }

            kept.AddRange(within);
            cursor = page.Cursor;
            if (kept.Count >= limit || cursor is null)
                break;
        }

        return new DrivePage(kept, cursor);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "drive_search: account {Account} dropped {Id} outside its root (parent {ParentId}, parent path {ParentPath}, drive {DriveId})")]
    private static partial void LogDropped(ILogger logger, string account, string id, string? parentId, string? parentPath, string? driveId);
}
