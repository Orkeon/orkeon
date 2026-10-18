using System.Text;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Configuration;
using Orkeon.Tools.Drive.Constants;
using Orkeon.Tools.Drive.Drives;
using Orkeon.Tools.Drive.Drives.Google;
using Orkeon.Tools.Drive.Dtos;
using Orkeon.Tools.Drive.Security;

namespace Orkeon.Tools.Drive.Tools;

/// <summary>Reads the text of a file, screened, a slice at a time.</summary>
[ToolContract("drive_read",
    Name = "drive_read",
    Description = "Read the text of a cloud drive file — a text file (txt, md, csv, json, xml, code...) or a Google Docs, Sheets or Slides file — screened for prompt injection, a slice at a time with `offset`. A PDF, an Office file or an image is downloaded with drive_download and read with the file tools. Needs the Read right.",
    Category = "Drive")]
internal sealed class DriveReadTool : ToolBase<DriveReadRequest, DriveReadResponse>
{
    private static readonly HashSet<string> TextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json", "application/xml", "application/x-yaml", "application/yaml", "application/javascript",
        "application/x-javascript", "application/typescript", "application/sql", "application/x-sh", "application/csv",
        "application/x-ndjson", "application/toml",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".log", ".ini", ".cfg",
        ".conf", ".toml", ".html", ".htm", ".css", ".js", ".mjs", ".ts", ".py", ".cs", ".java", ".go", ".rs", ".sql",
        ".sh", ".ps1", ".bat", ".rst", ".tex", ".srt", ".vtt",
    };

    private readonly DriveAccess _access;
    private readonly DriveContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public DriveReadTool(DriveAccess access, DriveContentScreen screen, ILogger<DriveReadTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(screen);
        _access = access;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DriveReadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Id))
            return "`id` is required: pass a file id from drive_list or drive_search";
        if (request.Offset is < 0)
            return "`offset` cannot be negative";
        return request.MaxChars is <= 0 ? "`max_chars` must be positive" : null;
    }

    /// <inheritdoc />
    protected override async Task<DriveReadResponse> ExecuteTypedAsync(DriveReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = _access.Authorize(request.Account, DriveRights.Read);
        var drive = _access.Drive(account);
        var item = await DriveAccess.ItemAsync(drive, request.Id, cancellationToken).ConfigureAwait(false);
        var text = await ReadTextAsync(drive, item, cancellationToken).ConfigureAwait(false);

        var screening = _screen.Screen(item.Name, text);
        if (screening.Withhold)
            text = "[Text withheld: the prompt-injection screen rejected this file. An operator can lift Orkeon:Tools:Drive:Screening:WithholdRejected.]";

        var offset = Math.Min(request.Offset ?? 0, text.Length);
        var maxChars = Math.Min(request.MaxChars ?? DriveDefaults.DefaultReadChars, DriveDefaults.MaxReadChars);
        var empty = new DriveReadResponse
        {
            Notice = DriveContentScreen.UntrustedNotice,
            Security = new DriveSecurityDto
            {
                Verdict = screening.Verdict,
                RiskScore = screening.RiskScore,
                Reasons = screening.Reasons,
                Withheld = screening.Withhold,
            },
            Account = account.Name,
            Id = item.Id,
            Name = item.Name,
            TotalChars = text.Length,
            Offset = offset,
            NextOffset = text.Length,
        };
        var slice = DriveToolHelpers.Slice(text, offset, maxChars, DriveToolHelpers.TextWidth(empty, DriveToolHelpers.ResultBudget(Name)));
        var end = offset + slice.Length;
        return empty with { Text = slice, NextOffset = end < text.Length ? end : null };
    }

    private static async Task<string> ReadTextAsync(IDrive drive, DriveItem item, CancellationToken cancellationToken)
    {
        if (item.IsFolder)
            throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' is a folder: list it with drive_list.");

        string? format = null;
        if (item.IsGoogleDocument)
        {
            format = GoogleExports.TextExport(item)?.Extension
                ?? throw new DriveToolException(DriveErrorCode.InvalidRequest, $"'{item.Name}' has no text export: download it with drive_download (as pdf or png).");
        }
        else if (!IsText(item))
        {
            throw new DriveToolException(
                DriveErrorCode.InvalidRequest,
                $"'{item.Name}' ({item.MimeType ?? "unknown type"}) is not a text file: download it with drive_download, then read it with the file tools.");
        }
        else if (item.Size > DriveDefaults.MaxReadBytes)
        {
            throw new DriveToolException(
                DriveErrorCode.TooLarge,
                $"'{item.Name}' holds {item.Size / 1024} KB, more than drive_read fetches ({DriveDefaults.MaxReadBytes / 1024} KB): download it with drive_download.");
        }

        var content = await drive.DownloadAsync(item, format, cancellationToken).ConfigureAwait(false);
        await using (content.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await content.Stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > DriveDefaults.MaxReadBytes)
                {
                    throw new DriveToolException(
                        DriveErrorCode.TooLarge,
                        $"'{item.Name}' is longer than drive_read fetches ({DriveDefaults.MaxReadBytes / 1024} KB): download it with drive_download.");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsText(DriveItem item)
    {
        if (item.MimeType is { } type
            && (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || TextMimeTypes.Contains(type)
                || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return TextExtensions.Contains(Path.GetExtension(item.Name));
    }
}
