using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.FileSystem.Constants.File;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for FileReadTool.
/// </summary>
public sealed class FileReadRequest
{
    /// <summary>The file path to read from.</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "The file path to read from", Example = "/workspace/config.json")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The text encoding to use (default: UTF-8).</summary>
    [JsonPropertyName("encoding")]
    [FieldSchema(Description = "The text encoding to use (default: UTF-8)", IsRequired = false, Example = "UTF-8", Default = "UTF-8", Enum = new[] { "UTF-8", "ASCII", "Unicode", "UTF-32" })]
    public string Encoding { get; set; } = FileDefaults.DefaultEncoding;

    /// <summary>Maximum number of characters to read (default: no limit).</summary>
    [JsonPropertyName("max_length")]
    [FieldSchema(Description = "Maximum number of characters to read (default: no limit). Applied after offset/limit: when it cuts a line range, only whole lines are kept and has_more is true, except that when the first line alone exceeds max_length it is cut by characters.", IsRequired = false, Example = 10000)]
    public int? MaxLength { get; set; }

    /// <summary>When true, attempts to serve content from the RaggableTree index if a module matches by path and SHA-256.</summary>
    [JsonPropertyName("use_raggable_cache")]
    [FieldSchema(Description = "Serve from RaggableTree index when available (path + SHA-256 match); falls back to disk otherwise.", IsRequired = false, Example = false, Default = false)]
    public bool UseRaggableCache { get; set; }

    /// <summary>First line to return (1-based).</summary>
    [JsonPropertyName("offset")]
    [FieldSchema(Description = "First line to return (1-based, default 1 when limit is given). With offset/limit the file is streamed and not read beyond the range; max_length then caps the range", IsRequired = false, Example = 1)]
    public int? Offset { get; set; }

    /// <summary>Maximum number of lines to return.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "Maximum number of lines to return (default: up to the end of the file); max_length caps the result further", IsRequired = false, Example = 100)]
    public int? Limit { get; set; }

    /// <summary>Initializes a new instance of <see cref="FileReadRequest"/>.</summary>
    public FileReadRequest() { }
}

/// <summary>
/// Strongly-typed response for FileReadTool.
/// </summary>
public sealed class FileReadResponse
{
    /// <summary>The text content of the file.</summary>
    [JsonPropertyName("content")]
    [ReturnSchema(Description = "The text content of the file", Example = "{\n  \"name\": \"config\"\n}")]
    public string Content { get; set; } = string.Empty;

    /// <summary>File metadata: size, extension, last modified, etc.</summary>
    [JsonPropertyName("file_info")]
    [ReturnSchema(Description = "File metadata: size, extension, last modified, etc.")]
    public Dictionary<string, object> FileInfo { get; init; } = [];

    /// <summary>Whether the content was truncated due to max_length.</summary>
    [JsonPropertyName("truncated")]
    [ReturnSchema(Description = "Whether the content was truncated due to max_length", Example = false)]
    public bool Truncated { get; set; }

    /// <summary>First line served (1-based).</summary>
    [JsonPropertyName("start_line")]
    [ReturnSchema(Description = "First line served (1-based)", Example = 1)]
    public int? StartLine { get; set; }

    /// <summary>Last line served.</summary>
    [JsonPropertyName("end_line")]
    [ReturnSchema(Description = "Last line served", Example = 100)]
    public int? EndLine { get; set; }

    /// <summary>Whether the file continues after the served range.</summary>
    [JsonPropertyName("has_more")]
    [ReturnSchema(Description = "Whether the file continues after the served range", Example = false)]
    public bool HasMore { get; set; }

    /// <summary>Whether the content was served from the RaggableTree index rather than disk.</summary>
    [JsonPropertyName("served_from_cache")]
    [ReturnSchema(Description = "True when the content was served from the RaggableTree index (path + SHA-256 match).", Example = false)]
    public bool ServedFromCache { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for reading content from files.
/// Supports text files, JSON, XML, and common document formats.
/// </summary>
public partial class FileReadTool : FileToolBase<FileReadRequest, FileReadResponse>
{
    private readonly IRaggableStore? _raggableStore;

    /// <inheritdoc />
    public override string Name => "file_read";

    /// <inheritdoc />
    public override string Description => "Read content from files: text, JSON, XML and the like. Parse .eml e-mail files with email_parser.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <summary>Initializes a new instance of <see cref="FileReadTool"/> with an optional RaggableTree store for cache lookups.</summary>
    public FileReadTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        IRaggableStore? raggableStore = null,
        ILogger<FileReadTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
        _raggableStore = raggableStore;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(FileReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path parameter is required";
        if (request.Offset is <= 0)
            return "Offset must be at least 1 (lines are 1-based)";
        if (request.Limit is <= 0)
            return "Limit must be at least 1";
        return null;
    }

    /// <inheritdoc />
    protected override Task<FileReadResponse> ExecuteTypedAsync(
        FileReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<FileReadResponse> ExecuteTypedCoreAsync()
        {
            // Validate path safety via the VFS.
            var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
            if (!pathValidation.IsAllowed)
                throw new InvalidOperationException(pathValidation.DenialReason ?? "Invalid or unsafe file path");

            var resolvedPath = pathValidation.ResolvedPath!;
            var displayPath = (_fileSystemService.ToVirtualPath(resolvedPath) ?? AbsolutizePath(request.Path)) ?? resolvedPath;
            var virtualPath = displayPath;

            var entry = await _fileSystemService.TryGetEntryAsync(virtualPath, cancellationToken).ConfigureAwait(false);
            if (entry is null || entry.Kind == VirtualEntryKind.Directory)
                throw new FileNotFoundException($"File not found: {displayPath}");

            var encoding = GetEncoding(request.Encoding);

            var (content, servedFromCache, range) = await LoadContentAsync(
                request, virtualPath, resolvedPath, encoding, cancellationToken).ConfigureAwait(false);

            bool truncated = false;
            if (request.MaxLength.HasValue && content.Length > request.MaxLength.Value)
            {
                if (range is { } r)
                    (content, range) = CapToWholeLines(r, request.MaxLength.Value);
                else
                    content = content.Substring(0, request.MaxLength.Value);
                truncated = true;
            }

            var createdUtc = (entry.CreationTime ?? entry.LastModified).UtcDateTime;
            var response = new FileReadResponse
            {
                Content = content,
                FileInfo = new Dictionary<string, object>
                {
                    ["size"] = entry.SizeBytes,
                    ["extension"] = System.IO.Path.GetExtension(virtualPath),
                    ["last_modified"] = Inv.ToString(entry.LastModified.UtcDateTime, FileDefaults.DisplayDateTimeFormat),
                    ["created"] = Inv.ToString(createdUtc, FileDefaults.DisplayDateTimeFormat),
                    ["full_path"] = displayPath
                },
                Truncated = truncated,
                StartLine = range?.Start,
                EndLine = range?.End,
                HasMore = range?.HasMore ?? false,
                ServedFromCache = servedFromCache
            };

            LogReadFile(displayPath, entry.SizeBytes);
            return response;
        }
    }

    private readonly record struct LineRange(IReadOnlyList<string> Lines, int Start, int End, bool HasMore);

    private async Task<(string Content, bool ServedFromCache, LineRange? Range)> LoadContentAsync(
        FileReadRequest request, string virtualPath, string resolvedPath, Encoding encoding, CancellationToken ct)
    {
        var ranged = request.Offset.HasValue || request.Limit.HasValue;
        var start = request.Offset ?? 1;

        if (request.UseRaggableCache && _raggableStore is not null)
        {
            var cached = await TryReadFromCacheAsync(virtualPath, resolvedPath, encoding, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                if (!ranged)
                    return (cached, true, null);
                using var cachedReader = new StringReader(cached);
                var cutRange = await ReadRangeAsync(cachedReader, start, request.Limit, ct).ConfigureAwait(false);
                return (string.Join('\n', cutRange.Lines), true, cutRange);
            }
        }

        if (!ranged)
            return (await ReadContentAsync(virtualPath, encoding, ct).ConfigureAwait(false), false, null);

        try
        {
            var stream = await _fileSystemService.OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false);
            await using var __stream = stream.ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
            var range = await ReadRangeAsync(reader, start, request.Limit, ct).ConfigureAwait(false);
            return (string.Join('\n', range.Lines), false, range);
        }
        catch (System.IO.IOException ex)
        {
            throw new System.IO.IOException($"Error reading file: {ex.Message}", ex);
        }
    }

    private static async Task<LineRange> ReadRangeAsync(TextReader reader, int start, int? limit, CancellationToken ct)
    {
        var lines = new List<string>();
        var lineNumber = 0;
        var hasMore = false;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (lineNumber < start)
                continue;
            if (lines.Count == limit)
            {
                hasMore = true;
                break;
            }
            lines.Add(line);
        }

        if (lines.Count == 0 && start > 1)
            throw new InvalidOperationException($"Offset {start} is beyond the end of the file ({lineNumber} lines)");

        return new LineRange(lines, start, lines.Count == 0 ? start : start + lines.Count - 1, hasMore);
    }

    private static (string Content, LineRange Range) CapToWholeLines(LineRange range, int maxLength)
    {
        var kept = 0;
        var length = 0;
        foreach (var line in range.Lines)
        {
            var next = length + (kept == 0 ? 0 : 1) + line.Length;
            if (next > maxLength)
                break;
            length = next;
            kept++;
        }

        if (kept == 0)
            return (range.Lines[0][..maxLength], range with { End = range.Start, HasMore = true });

        return (string.Join('\n', range.Lines.Take(kept)),
            range with { End = range.Start + kept - 1, HasMore = true });
    }

    private async Task<string> ReadContentAsync(string virtualPath, Encoding encoding, CancellationToken ct)
    {
        try
        {
            // Open a read stream via the VFS and decode with the requested encoding.
            var stream = await _fileSystemService.OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false);
            await using var __stream = stream.ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (System.IO.IOException ex)
        {
            throw new System.IO.IOException($"Error reading file: {ex.Message}", ex);
        }
    }

    private async Task<string?> TryReadFromCacheAsync(string virtualPath, string resolvedPath, Encoding encoding, CancellationToken ct)
    {
        if (_raggableStore is null) return null;

        // Use the physical path as the key for the RaggableStore (store is keyed by physical path in tests).
        var storeKey = resolvedPath;
        var module = await _raggableStore.GetModuleByPathAsync(storeKey, ct).ConfigureAwait(false);
        if (module is null || string.IsNullOrEmpty(module.Sha256) || string.IsNullOrEmpty(module.SourceSnippet))
            return null;

        string diskContent;
        try
        {
            diskContent = await ReadContentAsync(virtualPath, encoding, ct).ConfigureAwait(false);
        }
        catch (System.IO.IOException)
        {
            return null;
        }

        var diskHash = ComputeSha256(diskContent);
        return string.Equals(diskHash, module.Sha256, StringComparison.OrdinalIgnoreCase)
            ? module.SourceSnippet
            : null;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static Encoding GetEncoding(string encodingName)
    {
        return encodingName.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => System.Text.Encoding.UTF8,
            "ASCII" => System.Text.Encoding.ASCII,
            "UNICODE" or "UTF-16" or "UTF16" => System.Text.Encoding.Unicode,
            "UTF-32" or "UTF32" => System.Text.Encoding.UTF32,
            _ => System.Text.Encoding.UTF8
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully read file: {Path} ({Size} bytes)")]
    private partial void LogReadFile(string path, long size);
}
