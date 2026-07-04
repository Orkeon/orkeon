using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.FileSystem.Constants.File;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for FileWriteTool.
/// </summary>
public sealed class FileWriteRequest
{
    /// <summary>The file path to write to.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "The file path to write to", Example = "/workspace/output.txt")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The content to write to the file.</summary>
    [JsonPropertyName("content")]
    [FieldSchema(Description = "The content to write to the file", Example = "Hello, World!")]
    public string Content { get; set; } = string.Empty;

    /// <summary>The text encoding to use (default: UTF-8).</summary>
    [JsonPropertyName("encoding")]
    [FieldSchema(Description = "The text encoding to use (default: UTF-8)", IsRequired = false, Example = "UTF-8", Default = "UTF-8", Enum = new[] { "UTF-8", "ASCII", "Unicode", "UTF-32" })]
    public string Encoding { get; set; } = FileDefaults.DefaultEncoding;

    /// <summary>Whether to append to the file instead of overwriting (default: false).</summary>
    [JsonPropertyName("append")]
    [FieldSchema(Description = "Whether to append to the file instead of overwriting (default: false)", IsRequired = false, Example = false, Default = false)]
    public bool Append { get; set; }

    /// <summary>Whether to create a backup before writing (default: false).</summary>
    [JsonPropertyName("create_backup")]
    [FieldSchema(Description = "Whether to create a backup before writing (default: false)", IsRequired = false, Example = false, Default = false)]
    public bool CreateBackup { get; set; }

    /// <summary>Initializes a new instance of <see cref="FileWriteRequest"/>.</summary>
    public FileWriteRequest() { }
}

/// <summary>
/// Strongly-typed response for FileWriteTool.
/// </summary>
public sealed class FileWriteResponse
{
    /// <summary>Whether the file was written successfully.</summary>
    [JsonPropertyName("success")]
    [ReturnSchema(Description = "Whether the file was written successfully", Example = true)]
    public bool Success { get; set; }

    /// <summary>Absolute path of the written file.</summary>
    [JsonPropertyName("path")]
    [ReturnSchema(Description = "Absolute path of the written file", Example = "/workspace/output.txt")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Number of bytes written to the file.</summary>
    [JsonPropertyName("bytes_written")]
    [ReturnSchema(Description = "Number of bytes written to the file", Example = 1024)]
    public int BytesWritten { get; set; }

    /// <summary>Path of the backup file, if create_backup was true.</summary>
    [JsonPropertyName("backup_path")]
    [ReturnSchema(Description = "Path of the backup file, if create_backup was true", Example = "/workspace/output.backup_20240115_093000.txt")]
    public string? BackupPath { get; set; }

    /// <summary>
    /// Citation-block validation violations, populated when citation validation fails.
    /// <see langword="null"/> when validation passed or was not run.
    /// </summary>
    [JsonPropertyName("errors")]
    [ReturnSchema(Description = "Citation block validation violations; non-null and non-empty when the write is rejected due to fabricated SHA or code body.")]
    public ImmutableList<string>? Errors { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for writing content to files.
/// Supports creating new files or overwriting existing ones.
/// </summary>
public partial class FileWriteTool : FileToolBase<FileWriteRequest, FileWriteResponse>
{
    /// <inheritdoc />
    public override string Name => "file_write";

    /// <inheritdoc />
    public override string Description => "Write content to files. Can create new files or overwrite existing ones. Creates directories if needed.";

    private readonly ICitationBlockValidator? _citationValidator;

    /// <summary>Initializes a new instance of <see cref="FileWriteTool"/>.</summary>
    /// <param name="fileSystemService">VFS abstraction; required.</param>
    /// <param name="pathValidator">Path safety validator; required.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="citationValidator">
    /// Optional citation-block validator. When provided, any successful write whose content
    /// contains at least one citation fenced block (<c>// FQN  :</c> + <c>// SHA  :</c> headers)
    /// is validated against the RaggableTree store. On violation the response returns
    /// <c>Success = false</c> with <c>Errors</c> populated.
    /// </param>
    public FileWriteTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<FileWriteTool>? logger = null,
        ICitationBlockValidator? citationValidator = null)
        : base(fileSystemService, pathValidator, logger)
    {
        _citationValidator = citationValidator;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(FileWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path parameter is required";
        if (request.Content == null)
            return "Content parameter is required";
        return null;
    }

    /// <inheritdoc />
    protected override Task<FileWriteResponse> ExecuteTypedAsync(
        FileWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<FileWriteResponse> ExecuteTypedCoreAsync()
        {
            // Validate path safety via the VFS.
            var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Write);
            if (!pathValidation.IsAllowed)
                throw new InvalidOperationException(pathValidation.DenialReason ?? "Invalid or unsafe file path");

            var resolvedPath = pathValidation.ResolvedPath!;
            var encoding = GetEncoding(request.Encoding);
            var virtualFilePath = _fileSystemService.ToVirtualPath(resolvedPath) ?? request.Path;
            await EnsureDirectoryExistsAsync(virtualFilePath, cancellationToken).ConfigureAwait(false);

            var backupVirtualPath = await CreateBackupIfRequestedAsync(request, virtualFilePath, cancellationToken).ConfigureAwait(false);

            await WriteContentAsync(request, virtualFilePath, encoding, backupVirtualPath, cancellationToken).ConfigureAwait(false);

            var successResponse = BuildSuccessResponse(request, resolvedPath, encoding, backupVirtualPath);

            // Citation-block validation: only triggered when a validator is injected and the
            // content contains at least one citation block (must have both // FQN  : and // SHA  : headers).
            if (_citationValidator is not null && HasCitationHeaders(request.Content))
            {
                var validationResult = await _citationValidator
                    .ValidateAsync(request.Content, cancellationToken)
                    .ConfigureAwait(false);

                if (!validationResult.IsValid)
                {
                    // INFRA-4: log the violations at warning level so the
                    // diagnostic trail survives even when upstream propagation
                    // fails (the user can grep the runner-output instead of
                    // poking through C# code to find why fileWrite "failed
                    // with empty error").
                    LogCitationValidationRejected(
                        successResponse.Path,
                        validationResult.Violations.Length,
                        string.Join(" | ", validationResult.Violations));
                    return new FileWriteResponse
                    {
                        Success = false,
                        Path = successResponse.Path,
                        BytesWritten = successResponse.BytesWritten,
                        BackupPath = successResponse.BackupPath,
                        Errors = [.. validationResult.Violations],
                    };
                }
            }

            return successResponse;
        }
    }

    private async Task<string?> CreateBackupIfRequestedAsync(
        FileWriteRequest request, string virtualFilePath, CancellationToken ct)
    {
        if (!request.CreateBackup) return null;

        var fileExists = await _fileSystemService.ExistsAsync(virtualFilePath, ct).ConfigureAwait(false);
        if (!fileExists) return null;

        var backupVirtualPath = BuildBackupVirtualPath(virtualFilePath);

        await _fileSystemService.CopyAsync(virtualFilePath, backupVirtualPath, overwrite: true, ct).ConfigureAwait(false);

        LogCreatedBackup(backupVirtualPath);
        return backupVirtualPath;
    }

    private async Task WriteContentAsync(
        FileWriteRequest request, string virtualFilePath, Encoding encoding, string? backupVirtualPath, CancellationToken ct)
    {
        try
        {
            if (request.Append)
            {
                var appendStream = await _fileSystemService.OpenAppendStreamAsync(virtualFilePath, ct).ConfigureAwait(false);
                await using var __appendStream = appendStream.ConfigureAwait(false);
                var appendWriter = new System.IO.StreamWriter(appendStream, encoding, bufferSize: -1, leaveOpen: false);
                await using var __appendWriter = appendWriter.ConfigureAwait(false);
                await appendWriter.WriteAsync(request.Content.AsMemory(), ct).ConfigureAwait(false);
            }
            else
            {
                var writeStream = await _fileSystemService.OpenWriteStreamAsync(virtualFilePath, ct).ConfigureAwait(false);
                await using var __writeStream = writeStream.ConfigureAwait(false);
                var writer = new System.IO.StreamWriter(writeStream, encoding, bufferSize: -1, leaveOpen: false);
                await using var __writer = writer.ConfigureAwait(false);
                await writer.WriteAsync(request.Content.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch (System.IO.IOException ex)
        {
            await RestoreBackupAsync(backupVirtualPath, virtualFilePath, ct).ConfigureAwait(false);
            throw new System.IO.IOException($"Error writing file: {ex.Message}", ex);
        }
    }

    private async Task RestoreBackupAsync(string? backupVirtualPath, string virtualFilePath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(backupVirtualPath)) return;

        try
        {
            if (await _fileSystemService.ExistsAsync(backupVirtualPath, ct).ConfigureAwait(false))
            {
                await _fileSystemService.CopyAsync(backupVirtualPath, virtualFilePath, overwrite: true, ct).ConfigureAwait(false);
                await _fileSystemService.DeleteAsync(backupVirtualPath, recursive: false, ct).ConfigureAwait(false);
            }
        }
        catch (System.IO.IOException) { /* Best effort restore */ }
        catch (UnauthorizedAccessException) { /* Best effort restore */ }
    }

    private FileWriteResponse BuildSuccessResponse(
        FileWriteRequest request, string resolvedPath, Encoding encoding, string? backupVirtualPath)
    {
        var bytesWritten = encoding.GetByteCount(request.Content);
        var displayPath = _fileSystemService?.ToVirtualPath(resolvedPath) ?? request.Path;

        LogWroteFile(displayPath, bytesWritten, request.Append);

        return new FileWriteResponse
        {
            Success = true,
            Path = displayPath,
            BytesWritten = bytesWritten,
            BackupPath = backupVirtualPath
        };
    }

    private static string BuildBackupVirtualPath(string virtualFilePath)
    {
        var directory = Path.GetDirectoryName(virtualFilePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(virtualFilePath);
        var extension = Path.GetExtension(virtualFilePath);
        var timestamp = Inv.ToString(DateTime.UtcNow, FileDefaults.BackupTimestampFormat);
        return string.Concat(directory, "/", fileName, FileDefaults.BackupSuffixPrefix, timestamp, extension)
                     .Replace('\\', '/');
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="content"/> contains at least one
    /// fenced code block that has both <c>// FQN  :</c> and <c>// SHA  :</c> header lines,
    /// indicating it is a citation block that should be validated.
    /// </summary>
    private static bool HasCitationHeaders(string content) =>
        content.Contains("// FQN  :", StringComparison.Ordinal) &&
        content.Contains("// SHA  :", StringComparison.Ordinal);

    // UTF-8 without BOM — matches the VFS convention (IFileSystemService.WriteAllTextAsync)
    // and keeps tool output consumable by jq/python json.tool/etc. without preamble workarounds.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static Encoding GetEncoding(string encodingName)
    {
        return encodingName.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => Utf8NoBom,
            "ASCII" => System.Text.Encoding.ASCII,
            "UNICODE" or "UTF-16" or "UTF16" => System.Text.Encoding.Unicode,
            "UTF-32" or "UTF32" => System.Text.Encoding.UTF32,
            _ => Utf8NoBom
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created backup at: {BackupPath}")]
    private partial void LogCreatedBackup(string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully wrote to file: {Path} ({BytesWritten} bytes, Append: {Append})")]
    private partial void LogWroteFile(string path, int bytesWritten, bool append);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Citation validation rejected write to {Path} ({ViolationCount} violations): {Violations}")]
    private partial void LogCitationValidationRejected(string path, int violationCount, string violations);
}
