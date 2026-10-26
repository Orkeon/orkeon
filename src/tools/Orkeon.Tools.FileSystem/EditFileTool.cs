using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Editing;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

/// <summary>Strongly-typed request for EditFileTool.</summary>
public sealed class EditFileRequest
{
    /// <summary>The file to edit.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "The file to edit", Example = "/workspace/a.txt")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The text to replace.</summary>
    [JsonPropertyName("old_string")]
    [FieldSchema(Description = "The text to replace", Example = "foo")]
    public string OldString { get; set; } = string.Empty;

    /// <summary>The replacement text.</summary>
    [JsonPropertyName("new_string")]
    [FieldSchema(Description = "The replacement text", Example = "bar")]
    public string NewString { get; set; } = string.Empty;

    /// <summary>Replace every occurrence.</summary>
    [JsonPropertyName("replace_all")]
    [FieldSchema(Description = "Replace every occurrence", IsRequired = false, Example = false, Default = false)]
    public bool ReplaceAll { get; set; }

    /// <summary>Create a backup before writing.</summary>
    [JsonPropertyName("create_backup")]
    [FieldSchema(Description = "Create a backup before writing", IsRequired = false, Example = false, Default = false)]
    public bool CreateBackup { get; set; }
}

/// <summary>Strongly-typed response for EditFileTool.</summary>
public sealed class EditFileResponse
{
    /// <summary>Whether the edit succeeded.</summary>
    [JsonPropertyName("success")]
    [ReturnSchema(Description = "Whether the edit succeeded", Example = true)]
    public bool Success { get; set; }

    /// <summary>Path of the edited file.</summary>
    [JsonPropertyName("path")]
    [ReturnSchema(Description = "Path of the edited file", Example = "/workspace/a.txt")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Number of replacements made.</summary>
    [JsonPropertyName("replacements")]
    [ReturnSchema(Description = "Number of replacements made", Example = 1)]
    public int Replacements { get; set; }

    /// <summary>Size, extension and full path of the edited file.</summary>
    [JsonPropertyName("file_info")]
    [ReturnSchema(Description = "Size, extension and full path of the edited file")]
    public Dictionary<string, object> FileInfo { get; init; } = [];

    /// <summary>Path of the backup file, if one was created.</summary>
    [JsonPropertyName("backup_path")]
    [ReturnSchema(Description = "Path of the backup file", Example = "/workspace/a.backup_20240115_093000.txt")]
    public string? BackupPath { get; set; }

    /// <summary>Citation validation violations.</summary>
    [JsonPropertyName("errors")]
    [ReturnSchema(Description = "Citation block validation violations")]
    public ImmutableList<string>? Errors { get; set; }
}

/// <summary>Replaces a unique string in a file.</summary>
public sealed class EditFileTool : FileToolBase<EditFileRequest, EditFileResponse>
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf32LeBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BeBom = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <inheritdoc />
    public override string Name => "edit_file";

    /// <inheritdoc />
    public override string Description => "Replace a string in a file when it occurs exactly once.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Edit;

    private readonly ICitationBlockValidator? _citationValidator;
    private readonly IIndexInvalidation? _indexInvalidation;

    /// <summary>Initializes a new instance of <see cref="EditFileTool"/>.</summary>
    public EditFileTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<EditFileTool>? logger = null,
        ICitationBlockValidator? citationValidator = null,
        IIndexInvalidation? indexInvalidation = null)
        : base(fileSystemService, pathValidator, logger)
    {
        _citationValidator = citationValidator;
        _indexInvalidation = indexInvalidation;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EditFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path parameter is required";
        if (request.OldString.Length == 0)
            return "old_string must not be empty";
        if (request.OldString == request.NewString)
            return "old_string and new_string must differ";
        return null;
    }

    /// <inheritdoc />
    protected override Task<EditFileResponse> ExecuteTypedAsync(
        EditFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EditAsync(request, cancellationToken);
    }

    private async Task<EditFileResponse> EditAsync(EditFileRequest request, CancellationToken cancellationToken)
    {
        var readCheck = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        if (!readCheck.IsAllowed)
            throw new InvalidOperationException(readCheck.DenialReason ?? "Invalid or unsafe file path");
        var writeCheck = ResolveVirtualPath(request.Path, FileAccessRights.Write);
        if (!writeCheck.IsAllowed)
            throw new InvalidOperationException(writeCheck.DenialReason ?? "Invalid or unsafe file path");

        var resolvedPath = writeCheck.ResolvedPath!;
        var virtualPath = _fileSystemService.ToVirtualPath(resolvedPath) ?? AbsolutizePath(request.Path);

        var holderId = Guid.NewGuid().ToString("N");
        var lockedPaths = new[] { virtualPath };
        if (!FileEditLocks.TryAcquire(lockedPaths, holderId, out var heldPath))
            throw new InvalidOperationException($"'{heldPath}' is held by another edit in progress.");
        try
        {
            return await EditLockedAsync(request, virtualPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            FileEditLocks.Release(lockedPaths, holderId);
        }
    }

    /// <summary>The edit itself, under the lock of <paramref name="virtualPath"/>.</summary>
    private async Task<EditFileResponse> EditLockedAsync(EditFileRequest request, string virtualPath, CancellationToken cancellationToken)
    {
        var original = await _fileSystemService.TryReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File not found: {virtualPath}");

        var (encoding, bom) = DetectEncoding(original);
        var content = Decode(original, encoding, bom);
        var (edited, count) = ApplyEdit(content, request);
        var bytes = new byte[bom.Length + encoding.GetByteCount(edited)];
        bom.CopyTo(bytes, 0);
        encoding.GetBytes(edited, 0, edited.Length, bytes, bom.Length);

        string? backupPath = null;
        if (request.CreateBackup)
        {
            backupPath = await FreeBackupPathAsync(FileWriteTool.BuildBackupVirtualPath(virtualPath), cancellationToken).ConfigureAwait(false);
            await _fileSystemService.CopyAsync(virtualPath, backupPath, overwrite: false, cancellationToken).ConfigureAwait(false);
        }

        await WriteOrRestoreAsync(virtualPath, bytes, original, cancellationToken).ConfigureAwait(false);

        // The bytes are on disk either way: mark the path stale before the citation verdict.
        _indexInvalidation?.MarkDirty(virtualPath);

        var response = new EditFileResponse
        {
            Success = true,
            Path = virtualPath,
            Replacements = count,
            BackupPath = backupPath,
            FileInfo = new Dictionary<string, object>
            {
                ["size"] = bytes.Length,
                ["extension"] = System.IO.Path.GetExtension(virtualPath),
                ["full_path"] = virtualPath,
            },
        };

        if (_citationValidator is not null && FileWriteTool.HasCitationHeaders(request.NewString))
        {
            var verdict = await _citationValidator.ValidateAsync(request.NewString, cancellationToken).ConfigureAwait(false);
            if (!verdict.IsValid)
            {
                response.Success = false;
                response.Errors = [.. verdict.Violations];
            }
        }

        return response;
    }

    /// <summary>The text of <paramref name="original"/>; refuses a file its encoding cannot decode.</summary>
    private static string Decode(byte[] original, Encoding encoding, byte[] bom)
    {
        try
        {
            return encoding.GetString(original, bom.Length, original.Length - bom.Length);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(bom.Length == 0
                ? "The file is not valid UTF-8 and has no byte-order mark; it was left untouched"
                : $"The file is not valid {encoding.WebName.ToUpperInvariant()} (detected from its byte-order mark); it was left untouched");
        }
    }

    /// <summary>
    /// <paramref name="content"/> with the replacement of <paramref name="request"/> applied, and the
    /// number of occurrences replaced; refuses an absent or ambiguous <c>old_string</c>.
    /// </summary>
    private static (string Edited, int Count) ApplyEdit(string content, EditFileRequest request)
    {
        // A pure-CRLF file matches LF-written strings and keeps CRLF; any other file is matched literally.
        var crlf = content.Contains("\r\n", StringComparison.Ordinal)
            && !content.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n', StringComparison.Ordinal);
        var oldString = request.OldString;
        var newString = request.NewString;
        if (crlf)
        {
            content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            oldString = oldString.Replace("\r\n", "\n", StringComparison.Ordinal);
            newString = newString.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        var count = CountOccurrences(content, oldString);
        if (count == 0)
            throw new InvalidOperationException("old_string not found in the file");
        if (count > 1 && !request.ReplaceAll)
            throw new InvalidOperationException(
                $"old_string matches {count} times; make it unique or set replace_all");

        var edited = content.Replace(oldString, newString, StringComparison.Ordinal);
        if (crlf)
            edited = edited.Replace("\n", "\r\n", StringComparison.Ordinal);
        return (edited, count);
    }

    /// <summary>Writes <paramref name="bytes"/>; on a failure, writes <paramref name="original"/> back before rethrowing.</summary>
    private async Task WriteOrRestoreAsync(string virtualPath, byte[] bytes, byte[] original, CancellationToken cancellationToken)
    {
        try
        {
            await _fileSystemService.WriteAllBytesAsync(virtualPath, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception writeError)
        {
            try
            {
                await _fileSystemService.WriteAllBytesAsync(virtualPath, original, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception restoreError)
            {
                throw new IOException(
                    $"{writeError.Message}; restoring the original also failed: {restoreError.Message}", writeError);
            }
            throw;
        }
    }

    private async Task<string> FreeBackupPathAsync(string candidate, CancellationToken cancellationToken)
    {
        if (!await _fileSystemService.ExistsAsync(candidate, cancellationToken).ConfigureAwait(false))
            return candidate;
        var extension = System.IO.Path.GetExtension(candidate);
        var stem = candidate[..^extension.Length];
        return $"{stem}_{Guid.NewGuid():N}"[..(stem.Length + 9)] + extension;
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        for (var i = content.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = content.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    internal static (Encoding Encoding, byte[] Bom) DetectEncoding(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Utf32LeBom)) return (new UTF32Encoding(false, false, true), Utf32LeBom);
        if (span.StartsWith(Utf32BeBom)) return (new UTF32Encoding(true, false, true), Utf32BeBom);
        if (span.StartsWith(Utf8Bom)) return (new UTF8Encoding(false, true), Utf8Bom);
        if (span.StartsWith(Utf16BeBom)) return (new UnicodeEncoding(true, false, true), Utf16BeBom);
        if (span.StartsWith(Utf16LeBom)) return (new UnicodeEncoding(false, false, true), Utf16LeBom);
        return (new UTF8Encoding(false, true), []);
    }
}
