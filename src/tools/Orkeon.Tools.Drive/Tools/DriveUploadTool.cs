using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Uploads a file of the VFS to a drive.</summary>
[ToolContract("drive_upload",
    Name = "drive_upload",
    Description = "Upload a file of the virtual file system (e.g. /output/report.pdf) to a cloud drive folder (the account's root folder by default), or replace the content of an existing file with `replace_id`. A name already taken in the folder is refused. Needs the Write right.",
    Category = "Drive")]
internal sealed class DriveUploadTool : ToolBase<DriveUploadRequest, DriveUploadResponse>
{
    private readonly DriveAccess _access;
    private readonly IFileSystemService _fileSystem;

    /// <summary>Creates the tool.</summary>
    public DriveUploadTool(DriveAccess access, IFileSystemService fileSystem, ILogger<DriveUploadTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _access = access;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Path))
            return "`path` is required: the virtual path of the file to upload, e.g. /output/report.pdf";
        return request.Name is { } name && (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
            ? "`name` is a file name, without '/'"
            : null;
    }

    /// <inheritdoc />
    protected override async Task<DriveUploadResponse> ExecuteTypedAsync(DriveUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Write);
        var path = request.Path.Trim();
        var check = _fileSystem.ResolveAndValidate(path, FileAccessRights.Read);
        if (!check.IsAllowed)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"Cannot read '{path}': {(check.DenialReason ?? "the file is not readable").TrimEnd('.')}.");

        var entry = await _fileSystem.TryGetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (entry is null || entry.Kind != VirtualEntryKind.File)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{path}' is not a file of the virtual file system.");
        if (entry.SizeBytes > account.MaxUploadBytes)
        {
            throw new DriveToolException(
                DriveErrorCode.TooLarge,
                $"'{path}' holds {entry.SizeBytes / (1024 * 1024)} MB; drive account '{account.Name}' accepts at most {account.MaxUploadBytes / (1024 * 1024)} MB (MaxUploadMegabytes).");
        }

        var drive = _access.Drive(account);
        var source = await _fileSystem.OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var fileName = string.IsNullOrWhiteSpace(request.Name) ? path[(path.LastIndexOf('/') + 1)..] : request.Name.Trim();
            var upload = new DriveUpload(source, entry.SizeBytes, MimeTypes.Of(fileName));

            if (!string.IsNullOrWhiteSpace(request.ReplaceId))
            {
                var existing = await DriveAccess.ItemAsync(drive, request.ReplaceId, cancellationToken).ConfigureAwait(false);
                if (existing.IsFolder || existing.IsGoogleDocument)
                    throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{existing.Name}' is a {(existing.IsFolder ? "folder" : "Google document")}: only a stored file has content to replace.");
                var replaced = await drive.ReplaceContentAsync(existing.Id, upload with { MimeType = existing.MimeType ?? upload.MimeType }, cancellationToken).ConfigureAwait(false);
                return new DriveUploadResponse { Account = account.Name, Replaced = true, Item = DriveToolHelpers.ToDto(replaced, withUrl: true) };
            }

            var folder = await DriveAccess.FolderAsync(drive, request.FolderId, cancellationToken).ConfigureAwait(false);
            var created = await drive.UploadAsync(folder.Id, FileNames.Sanitize(fileName), upload, cancellationToken).ConfigureAwait(false);
            return new DriveUploadResponse { Account = account.Name, Replaced = false, Item = DriveToolHelpers.ToDto(created, withUrl: true) };
        }
    }
}
