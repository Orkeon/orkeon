using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Dtos;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Downloads a file into a virtual directory.</summary>
[ToolContract("drive_download",
    Name = "drive_download",
    Description = "Download a cloud drive file into a virtual directory such as /output/downloads (a Google document is exported, to `format` or its default); the name is sanitized and never overwrites. Read it afterwards with the file tools. Needs the Read right and a writable mount.",
    Category = "Drive")]
internal sealed class DriveDownloadTool : ToolBase<DriveDownloadRequest, DriveDownloadResponse>
{
    private readonly DriveAccess _access;
    private readonly IFileSystemService _fileSystem;

    /// <summary>Creates the tool.</summary>
    public DriveDownloadTool(DriveAccess access, IFileSystemService fileSystem, ILogger<DriveDownloadTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _access = access;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveDownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Id))
            return "`id` is required: pass a file id from drive_list or drive_search";
        return string.IsNullOrWhiteSpace(request.Directory) ? "`directory` is required, e.g. /output/downloads" : null;
    }

    /// <inheritdoc />
    protected override async Task<DriveDownloadResponse> ExecuteTypedAsync(DriveDownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Read);
        var directory = request.Directory.Trim().TrimEnd('/');
        // Every file written is a new one (nothing is overwritten): the mount must let the crew create.
        var check = _fileSystem.ResolveAndValidate(directory + "/_", FileAccessRights.Write | FileAccessRights.Create);
        if (!check.IsAllowed)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"Cannot write into '{directory}': {(check.DenialReason ?? "the directory is not writable").TrimEnd('.')}.");

        var drive = _access.Drive(account);
        var item = await DriveAccess.ItemAsync(drive, request.Id, cancellationToken).ConfigureAwait(false);
        var content = await drive.DownloadAsync(item, request.Format, cancellationToken).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            await _fileSystem.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            var path = await FileNames.FreePathAsync(_fileSystem, directory, content.FileName, cancellationToken).ConfigureAwait(false);
            long written;
            var target = await _fileSystem.OpenWriteStreamAsync(path, cancellationToken).ConfigureAwait(false);
            await using (target.ConfigureAwait(false))
            {
                await content.Stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                written = target.CanSeek ? target.Length : content.Length ?? 0;
            }

            return new DriveDownloadResponse
            {
                Account = account.Name,
                Id = item.Id,
                Path = path,
                Bytes = written,
                MimeType = content.MimeType,
            };
        }
    }
}
