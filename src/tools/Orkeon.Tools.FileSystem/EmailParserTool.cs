using System.Text.RegularExpressions;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

// ── Request / Response records ────────────────────────────────────────
/// <summary>Request parameters for the email_parser tool.</summary>
public record EmailParserRequest
{
    /// <summary>The path to the email file (.eml or .msg).</summary>
    [FieldSchema(Description = "The path to the email file (.eml or .msg)", Example = "/inbox/newsletter.eml")]
    public string Path { get; init; } = "";

    /// <summary>Whether to extract attachment information (default: true).</summary>
    [FieldSchema(Description = "Whether to extract attachment information (default: true)", Example = true)]
    public bool ExtractAttachments { get; init; } = true;

    /// <summary>Whether to parse HTML content to plain text (default: true).</summary>
    [FieldSchema(Description = "Whether to parse HTML content to plain text (default: true)", Example = true)]
    public bool ParseHtml { get; init; } = true;
}

/// <summary>Metadata about an email attachment returned by the email_parser tool.</summary>
public record EmailAttachmentInfo
{
    /// <summary>Attachment filename.</summary>
    [ReturnSchema(Description = "Attachment filename", Example = "invoice_2024.pdf")]
    public string? Filename { get; init; }

    /// <summary>MIME content type (e.g., application/pdf).</summary>
    [ReturnSchema(Description = "MIME content type (e.g., application/pdf)", Example = "application/pdf")]
    public string? ContentType { get; init; }

    /// <summary>Encoded size in bytes (base64).</summary>
    [ReturnSchema(Description = "Encoded size in bytes (base64)", Example = 34560)]
    public int? EncodedSize { get; init; }

    /// <summary>Estimated decoded size in bytes.</summary>
    [ReturnSchema(Description = "Estimated decoded size in bytes", Example = 25920)]
    public int? EstimatedSize { get; init; }
}

/// <summary>Response returned by the email_parser tool containing parsed email data.</summary>
public record EmailParserResponse
{
    /// <summary>Sender email address.</summary>
    [ReturnSchema(Description = "Sender email address", Example = "alice@example.com")]
    public string From { get; init; } = "";

    /// <summary>Recipient email addresses.</summary>
    [ReturnSchema(Description = "Recipient email addresses")]
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>CC recipient email addresses.</summary>
    [ReturnSchema(Description = "CC recipient email addresses")]
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>Email subject line.</summary>
    [ReturnSchema(Description = "Email subject line", Example = "Q3 Project Status Update")]
    public string Subject { get; init; } = "";

    /// <summary>Date the email was sent.</summary>
    [ReturnSchema(Description = "Date the email was sent", Example = "Mon, 15 Jan 2024 09:30:00 +0000")]
    public string Date { get; init; } = "";

    /// <summary>Email body text (HTML converted to plain text if parse_html is true).</summary>
    [ReturnSchema(Description = "Email body text (HTML converted to plain text if parse_html is true)", Example = "Hi team,\nPlease find the latest status report attached.")]
    public string Body { get; init; } = "";

    /// <summary>List of attachment metadata.</summary>
    [ReturnSchema(Description = "List of attachment metadata")]
    public IReadOnlyList<EmailAttachmentInfo> Attachments { get; init; } = [];

    /// <summary>Raw email headers as key-value pairs.</summary>
    [ReturnSchema(Description = "Raw email headers as key-value pairs")]
    public Dictionary<string, string> Headers { get; init; } = [];

    /// <summary>Email priority level (Normal, High, Low).</summary>
    [ReturnSchema(Description = "Email priority level (Normal, High, Low)", Example = "Normal")]
    public string Priority { get; init; } = "Normal";
}

/// <summary>
/// Tool for parsing email files (.eml, .msg) and extracting structured data.
/// </summary>
[ToolContract("email_parser",
    Name = "email_parser",
    Description = "Parse email files (.eml, .msg) and extract structured data including headers, body, and attachments information.",
    Category = "File System")]
public partial class EmailParserTool : FileToolBase<EmailParserRequest, EmailParserResponse>
{
    private static readonly string[] s_bodyDelimiters = ["\r\n\r\n", "\n\n"];
    private static readonly string[] s_lineDelimiters = ["\r\n", "\n"];

    /// <summary>Initializes a new instance of <see cref="EmailParserTool"/>.</summary>
    public EmailParserTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        ILogger<EmailParserTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailParserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var pathResult = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        if (!pathResult.IsAllowed || string.IsNullOrEmpty(pathResult.ResolvedPath))
            return pathResult.DenialReason ?? "path validation failed";

#pragma warning disable CA1308 // extension is normalized to lowercase to match the ".eml"/".msg" literals; lowercase is the required match form here
        var extension = System.IO.Path.GetExtension(request.Path).ToLowerInvariant();
#pragma warning restore CA1308
        if (extension != ".eml" && extension != ".msg")
            return "File must be .eml or .msg format";

        return null;
    }

    /// <inheritdoc />
    protected override Task<EmailParserResponse> ExecuteTypedAsync(
        EmailParserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<EmailParserResponse> ExecuteCoreAsync()
        {
#pragma warning disable CA1308 // extension is normalized to lowercase to drive the ".eml"/".msg" branch selection; lowercase is the required match form here
            var extension = System.IO.Path.GetExtension(request.Path).ToLowerInvariant();
#pragma warning restore CA1308

            var pathResult = ResolveVirtualPath(request.Path, FileAccessRights.Read);
            if (!pathResult.IsAllowed || string.IsNullOrEmpty(pathResult.ResolvedPath))
                throw new InvalidOperationException(pathResult.DenialReason ?? "path validation failed");

            var virtualPath = _fileSystemService.ToVirtualPath(pathResult.ResolvedPath) ?? request.Path;
            var content = await _fileSystemService.TryReadAllTextAsync(virtualPath, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException($"File not found: {request.Path}");

            // Parse email based on format
            var emailData = extension == ".eml"
                ? ParseEmlFile(content, request.ExtractAttachments, request.ParseHtml)
                : ParseMsgFileSimplified();

            LogParsedEmail(request.Path);

            return emailData;
        }
    }

    private static EmailParserResponse ParseEmlFile(string content, bool extractAttachments, bool parseHtml)
    {
        var result = new Dictionary<string, object>();

        // Split headers and body
        var parts = content.Split(s_bodyDelimiters, 2, StringSplitOptions.None);
        var headerSection = parts.Length > 0 ? parts[0] : "";
        var bodySection = parts.Length > 1 ? parts[1] : "";

        var headers = ParseEmailHeaders(headerSection, result);

        result["body"] = ExtractEmailBody(bodySection, headers, parseHtml);

        var attachments = new List<EmailAttachmentInfo>();
        if (extractAttachments)
            attachments = ExtractAttachmentInfoTyped(bodySection, headers);

        result.TryAdd("from", "");
        result.TryAdd("to", new List<string>());
        result.TryAdd("cc", new List<string>());
        result.TryAdd("subject", "");
        result.TryAdd("date", "");

        return new EmailParserResponse
        {
            From = result.TryGetValue("from", out var f) ? f?.ToString() ?? "" : "",
            To = result.TryGetValue("to", out var t) && t is List<string> toList ? toList : [],
            Cc = result.TryGetValue("cc", out var c) && c is List<string> ccList ? ccList : [],
            Subject = result.TryGetValue("subject", out var s) ? s?.ToString() ?? "" : "",
            Date = result.TryGetValue("date", out var d) ? d?.ToString() ?? "" : "",
            Body = result.TryGetValue("body", out var b) ? b?.ToString() ?? "" : "",
            Attachments = attachments,
            Headers = headers,
            Priority = DeterminePriority(headers)
        };
    }

    private static Dictionary<string, string> ParseEmailHeaders(string headerSection, Dictionary<string, object> result)
    {
        var headers = new Dictionary<string, string>();
        var headerLines = headerSection.Split(s_lineDelimiters, StringSplitOptions.None);
        var currentHeader = "";
        var currentValueBuilder = new System.Text.StringBuilder();

        foreach (var line in headerLines)
        {
            if (IsHeaderContinuation(line))
            {
                currentValueBuilder.Append(' ');
                currentValueBuilder.Append(line.Trim());
                continue;
            }

            if (!line.Contains(':', StringComparison.Ordinal))
                continue;

            // Save previous header if exists
            if (!string.IsNullOrEmpty(currentHeader))
                SaveHeader(headers, result, currentHeader, currentValueBuilder.ToString());

            // Start new header
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            currentHeader = line.Substring(0, colonIndex).Trim();
            currentValueBuilder.Clear();
            currentValueBuilder.Append(line.Substring(colonIndex + 1).Trim());
        }

        // Save last header
        if (!string.IsNullOrEmpty(currentHeader))
            SaveHeader(headers, result, currentHeader, currentValueBuilder.ToString());

        return headers;
    }

    private static bool IsHeaderContinuation(string line)
    {
        return !string.IsNullOrEmpty(line) && (line[0] == ' ' || line[0] == '\t');
    }

    private static void SaveHeader(Dictionary<string, string> headers, Dictionary<string, object> result, string headerName, string headerValue)
    {
        headers[headerName] = headerValue;
        ProcessStandardHeader(result, headerName, headerValue);
    }

    private static void ProcessStandardHeader(Dictionary<string, object> result, string header, string value)
    {
#pragma warning disable CA1308 // header is normalized to the lowercase switch keys ("from", "to", ...), a required match form, not a comparison normalization
        switch (header.ToLowerInvariant())
#pragma warning restore CA1308
        {
            case "from":
                result["from"] = ExtractEmailAddress(value);
                break;
            case "to":
                result["to"] = ExtractEmailAddresses(value);
                break;
            case "cc":
                result["cc"] = ExtractEmailAddresses(value);
                break;
            case "subject":
                result["subject"] = DecodeEncodedWord(value);
                break;
            case "date":
                result["date"] = value;
                break;
        }
    }

    private static string ExtractEmailAddress(string value)
    {
        var match = EmailAddressRegex().Match(value);
        if (!match.Success)
            return value;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static List<string> ExtractEmailAddresses(string value)
    {
        var addresses = new List<string>();
        var parts = value.Split(',');
        foreach (var part in parts)
        {
            var address = ExtractEmailAddress(part.Trim());
            if (!string.IsNullOrEmpty(address))
            {
                addresses.Add(address);
            }
        }
        return addresses;
    }

    private static string DecodeEncodedWord(string value)
    {
        // Simple implementation for encoded-word (RFC 2047)
        // Format: =?charset?encoding?encoded-text?=
        return EncodedWordRegex().Replace(value, match =>
        {
            try
            {
                var charset = match.Groups[1].Value;
                var encoding = match.Groups[2].Value.ToUpperInvariant();
                var encodedText = match.Groups[3].Value;

                if (encoding == "B")
                {
                    // Base64 encoding
                    var bytes = Convert.FromBase64String(encodedText);
                    return System.Text.Encoding.GetEncoding(charset).GetString(bytes);
                }
                else if (encoding == "Q")
                {
                    // Quoted-printable encoding
                    return DecodeQuotedPrintable(encodedText);
                }
            }
            catch (FormatException)
            {
                // If decoding fails, return original
            }
            catch (ArgumentException)
            {
                // If charset is invalid, return original
            }
            return match.Value;
        });
    }

    private static string DecodeQuotedPrintable(string input)
    {
        // Simple quoted-printable decoder
        var result = QuotedPrintableRegex().Replace(input, match =>
        {
            var hex = match.Groups[1].Value;
            return ((char)Convert.ToInt32(hex, 16)).ToString();
        });
        return result.Replace("_", " ", StringComparison.Ordinal);
    }

    private static string ExtractEmailBody(string bodySection, Dictionary<string, string> headers, bool parseHtml)
    {
        if (!headers.TryGetValue("Content-Type", out var contentType) ||
            !contentType.Contains("multipart", StringComparison.Ordinal))
            return bodySection.Trim();

        var boundaryMatch = BoundaryRegex().Match(contentType);
        if (!boundaryMatch.Success)
            return bodySection.Trim();

        var boundary = boundaryMatch.Groups[1].Value;
        var parts = bodySection.Split(["--" + boundary], StringSplitOptions.RemoveEmptyEntries);

        var extracted = ExtractBodyFromParts(parts, parseHtml);
        return extracted ?? bodySection.Trim();
    }

    private static string? ExtractBodyFromParts(string[] parts, bool parseHtml)
    {
        foreach (var part in parts)
        {
            if (part.Contains("Content-Type: text/plain", StringComparison.Ordinal))
            {
                var body = ExtractPartBody(part);
                if (body != null)
                    return body;
            }

            if (parseHtml && part.Contains("Content-Type: text/html", StringComparison.Ordinal))
            {
                var body = ExtractPartBody(part);
                if (body != null)
                    return StripHtmlTags(body);
            }
        }

        return null;
    }

    private static string? ExtractPartBody(string part)
    {
        var bodyStart = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (bodyStart == -1)
            bodyStart = part.IndexOf("\n\n", StringComparison.Ordinal);

        return bodyStart != -1 ? part.Substring(bodyStart).Trim() : null;
    }

    private static string StripHtmlTags(string html)
    {
        // Simple HTML tag removal
        var text = HtmlTagRegex().Replace(html, " ");
        text = WhitespaceRegex().Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    private static List<EmailAttachmentInfo> ExtractAttachmentInfoTyped(string bodySection, Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Content-Type", out var contentType)
            || !contentType.Contains("multipart", StringComparison.Ordinal))
            return [];

        var boundaryMatch = BoundaryRegex().Match(contentType);
        if (!boundaryMatch.Success)
            return [];

        var boundary = boundaryMatch.Groups[1].Value;
        var parts = bodySection.Split(["--" + boundary], StringSplitOptions.RemoveEmptyEntries);

        return parts
            .Where(part => part.Contains("Content-Disposition: attachment", StringComparison.Ordinal))
            .Select(ParseAttachmentPartTyped)
            .Where(a => a.Filename != null || a.ContentType != null)
            .ToList();
    }

    private static EmailAttachmentInfo ParseAttachmentPartTyped(string part)
    {
        var filenameMatch = FilenameRegex().Match(part);
        var typeMatch = ContentTypeRegex().Match(part);

        int? encodedSize = null;
        int? estimatedSize = null;

        var bodyStart = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (bodyStart == -1)
            bodyStart = part.IndexOf("\n\n", StringComparison.Ordinal);

        if (bodyStart != -1)
        {
            var encodedContent = part.Substring(bodyStart).Trim();
            encodedSize = encodedContent.Length;
            estimatedSize = (int)(encodedContent.Length * 0.75);
        }

        return new EmailAttachmentInfo
        {
            Filename = filenameMatch.Success ? filenameMatch.Groups[1].Value : null,
            ContentType = typeMatch.Success ? typeMatch.Groups[1].Value.Trim() : null,
            EncodedSize = encodedSize,
            EstimatedSize = estimatedSize
        };
    }

    private static string DeterminePriority(Dictionary<string, string> headers)
    {
        // Check various priority headers
        if (headers.TryGetValue("X-Priority", out var xPriority))
        {
            return xPriority switch
            {
                "1" or "2" => "High",
                "3" => "Normal",
                "4" or "5" => "Low",
                _ => "Normal"
            };
        }

        if (headers.TryGetValue("Importance", out var importance))
        {
#pragma warning disable CA1308 // importance is normalized to the lowercase switch keys ("high", "low"), a required match form, not a comparison normalization
            return importance.ToLowerInvariant() switch
#pragma warning restore CA1308
            {
                "high" => "High",
                "low" => "Low",
                _ => "Normal"
            };
        }

        return "Normal";
    }

    private EmailParserResponse ParseMsgFileSimplified()
    {
        // Simplified parsing for .msg files
        // In a real implementation, we'd use a library like MsgKit
        LogMsgFileSimplifiedParsing();

        return new EmailParserResponse
        {
            From = "",
            To = [],
            Cc = [],
            Subject = "MSG file (simplified parsing)",
            Date = Inv.ToString(DateTime.UtcNow, "yyyy-MM-dd HH:mm:ss 'UTC'"),
            Body = "MSG file content cannot be fully parsed without specialized library",
            Attachments = [],
            Headers = [],
            Priority = "Normal"
        };
    }

    [GeneratedRegex(@"<(.+?)>|([^\s]+@[^\s]+)")]
    private static partial Regex EmailAddressRegex();

    [GeneratedRegex(@"=\?([^?]+)\?([BQbq])\?([^?]+)\?=")]
    private static partial Regex EncodedWordRegex();

    [GeneratedRegex(@"=([0-9A-F]{2})")]
    private static partial Regex QuotedPrintableRegex();

    [GeneratedRegex(@"boundary=""?([^"";\s]+)""?")]
    private static partial Regex BoundaryRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"filename=""?([^"";\r\n]+)""?")]
    private static partial Regex FilenameRegex();

    [GeneratedRegex(@"Content-Type:\s*([^;\r\n]+)")]
    private static partial Regex ContentTypeRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully parsed email file: {Path}")]
    private partial void LogParsedEmail(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MSG file parsing is simplified. Consider using a specialized library for full support.")]
    private partial void LogMsgFileSimplifiedParsing();
}
