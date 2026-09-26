using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mime;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Saves attachments of a message into a virtual directory.</summary>
[ToolContract("email_save_attachment",
    Name = "email_save_attachment",
    Description = "Save one attachment (by index from email_read) or all of them into a virtual directory such as /output/attachments; file names are sanitized and never overwrite. Read them afterwards with the file tools. Needs the Read right and a writable mount.",
    Category = "Email")]
internal sealed class EmailSaveAttachmentTool : ToolBase<EmailSaveAttachmentRequest, EmailSaveAttachmentResponse>
{
    private readonly EmailAccess _access;
    private readonly IFileSystemService _fileSystem;

    /// <summary>Creates the tool.</summary>
    public EmailSaveAttachmentTool(EmailAccess access, IFileSystemService fileSystem, ILogger<EmailSaveAttachmentTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _access = access;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Edit;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailSaveAttachmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Id))
            return "`id` is required: pass a message id from email_search";
        return string.IsNullOrWhiteSpace(request.Directory) ? "`directory` is required, e.g. /output/attachments" : null;
    }

    /// <inheritdoc />
    protected override async Task<EmailSaveAttachmentResponse> ExecuteTypedAsync(EmailSaveAttachmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, EmailRights.Read);
        var directory = request.Directory.Trim().TrimEnd('/');
        // Every file written is a new one (nothing is overwritten): the mount must let the crew create.
        var check = _fileSystem.ResolveAndValidate(directory + "/_", FileAccessRights.Write | FileAccessRights.Create);
        if (!check.IsAllowed)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, $"Cannot write into '{directory}': {(check.DenialReason ?? "the directory is not writable").TrimEnd('.')}.");

        using var fetched = await _access.Mailbox(account).GetMessageAsync(request.Id.Trim(), cancellationToken).ConfigureAwait(false);
        var attachments = MimeMessageReader.ReadAttachments(fetched.Message);
        var selected = request.Index is { } index
            ? attachments.Where(entry => entry.Index == index).ToList()
            : attachments.ToList();
        if (selected.Count == 0)
        {
            throw new EmailToolException(
                EmailErrorCode.InvalidRequest,
                request.Index is null ? "This message has no attachment." : $"This message has no attachment at index {request.Index}: see email_read.");
        }

        await _fileSystem.CreateDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        var saved = new List<SavedAttachmentDto>(selected.Count);
        foreach (var entry in selected)
        {
            var path = await FreePathAsync(directory, entry.FileName, cancellationToken).ConfigureAwait(false);
            var stream = await _fileSystem.OpenWriteStreamAsync(path, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                await MimeMessageReader.WriteAttachmentAsync(entry, stream, cancellationToken).ConfigureAwait(false);
            }

            saved.Add(new SavedAttachmentDto { Index = entry.Index, FileName = path[(path.LastIndexOf('/') + 1)..], Path = path });
        }

        return new EmailSaveAttachmentResponse { Account = account.Name, Id = fetched.Id, Saved = saved };
    }

    private async Task<string> FreePathAsync(string directory, string fileName, CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var candidate = AttachmentNames.Unique(fileName, taken);
            var path = $"{directory}/{candidate}";
            if (!await _fileSystem.ExistsAsync(path, cancellationToken).ConfigureAwait(false))
                return path;
        }
    }
}
