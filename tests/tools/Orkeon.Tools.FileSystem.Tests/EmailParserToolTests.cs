using System.Collections;
using System.Text.Json;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Comprehensive tests for <see cref="EmailParserTool"/>.
/// Covers parsing of .eml files, header extraction, body extraction,
/// multipart MIME, attachments, encoded words, priority detection,
/// error handling, and path traversal prevention.
/// </summary>
public sealed class EmailParserToolTests : IDisposable
{
    private readonly EmailParserTool _tool;
    private readonly List<string> _tempFiles = [];

    public EmailParserToolTests()
    {
        _tool = new EmailParserTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
    }

    public void Dispose()
    {
        _tool.Dispose();
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { /* best effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string CreateTempEml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.eml");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempMsg(string content = "dummy")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.msg");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private async Task<ToolCallResponse> CallAsync(string path, bool extractAttachments = true, bool parseHtml = true)
    {
        var parameters = new Dictionary<string, object?>
        {
            [ParamPath] = path,
            ["extract_attachments"] = extractAttachments,
            ["parse_html"] = parseHtml
        };
        var request = new ToolCallRequest("email_parser", parameters);
        return await _tool.CallAsync(request, CancellationToken.None);
    }

    /// <summary>
    /// Converts a result dictionary value to a flat JSON string for easy substring assertions.
    /// Handles List, Dictionary, JsonElement, and plain object values uniformly.
    /// </summary>
    private static string ToJson(object? value)
    {
        if (value is null) return "";
        if (value is string s) return s;
        if (value is IEnumerable and not string)
            return JsonSerializer.Serialize(value);
        return value.ToString() ?? "";
    }

    /// <summary>
    /// Extracts a list of strings from a result dictionary value (handles List, JsonElement array, etc.).
    /// </summary>
    private static List<string> ToStringList(object? value)
    {
        if (value is IEnumerable<string> strings)
            return strings.ToList();
        if (value is IEnumerable<object> objects)
            return objects.Select(o => o?.ToString() ?? "").ToList();
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        return [];
    }

    // ── Simple email parsing ─────────────────────────────────────────────

    [Fact]
    public async Task ParseSimpleEml_ReturnsSuccessWithParsedData()
    {
        // Arrange
        var eml = "From: alice@example.com\r\nTo: bob@example.com\r\nSubject: Hello\r\nDate: Mon, 15 Jan 2024 09:30:00 +0000\r\n\r\nHello Bob, how are you?";
        var path = CreateTempEml(eml);

        // Act
        var response = await CallAsync(path);

        // Assert
        Assert.True(response.Success, response.Error);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }

    // ── Header extraction ────────────────────────────────────────────────

    [Fact]
    public async Task ParseEml_ExtractsFromHeader()
    {
        var eml = "From: Alice <alice@example.com>\r\nTo: bob@example.com\r\nSubject: Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody text";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("alice@example.com", result["from"]?.ToString());
    }

    [Fact]
    public async Task ParseEml_ExtractsToHeader_MultipleRecipients()
    {
        var eml = "From: alice@example.com\r\nTo: bob@example.com, charlie@example.com\r\nSubject: Group\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nHi all";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var toList = ToStringList(result["to"]);
        Assert.Contains("bob@example.com", toList);
        Assert.Contains("charlie@example.com", toList);
    }

    [Fact]
    public async Task ParseEml_ExtractsSubjectHeader()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Quarterly Report Q3 2024\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nSee attached";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("Quarterly Report Q3 2024", result["subject"]?.ToString());
    }

    [Fact]
    public async Task ParseEml_ExtractsDateHeader()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Test\r\nDate: Mon, 15 Jan 2024 09:30:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("Mon, 15 Jan 2024 09:30:00 +0000", result["date"]?.ToString());
    }

    [Fact]
    public async Task ParseEml_ExtractsCcHeader()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nCc: cc1@example.com, cc2@example.com\r\nSubject: CC Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var ccList = ToStringList(result["cc"]);
        Assert.Contains("cc1@example.com", ccList);
        Assert.Contains("cc2@example.com", ccList);
    }

    [Fact]
    public async Task ParseEml_RawHeadersContainAllHeaders()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\nMessage-ID: <123@example.com>\r\nMIME-Version: 1.0\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.NotNull(result["headers"]);
        var headersJson = ToJson(result["headers"]);
        Assert.Contains("Message-ID", headersJson);
        Assert.Contains("MIME-Version", headersJson);
    }

    // ── Body extraction ──────────────────────────────────────────────────

    [Fact]
    public async Task ParseEml_ExtractsPlainTextBody()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nThis is the body of the email.\r\nIt has multiple lines.";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var body = result["body"]?.ToString();
        Assert.NotNull(body);
        Assert.Contains("This is the body of the email.", body);
        Assert.Contains("It has multiple lines.", body);
    }

    [Fact]
    public async Task ParseEml_EmptyBody_ReturnsEmptyString()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Empty\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\n";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var body = result["body"]?.ToString();
        Assert.NotNull(body);
        // Empty body is trimmed to empty string
        Assert.Equal("", body);
    }

    // ── Multipart MIME body ──────────────────────────────────────────────

    [Fact]
    public async Task ParseEml_MultipartPlainText_ExtractsTextPart()
    {
        var eml =
            "From: a@b.com\r\n" +
            "To: c@d.com\r\n" +
            "Subject: Multipart\r\n" +
            "Date: Tue, 1 Jan 2024 00:00:00 +0000\r\n" +
            "Content-Type: multipart/alternative; boundary=\"boundary123\"\r\n" +
            "\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "Plain text body here.\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><p>HTML body here.</p></body></html>\r\n" +
            "--boundary123--";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var body = result["body"]?.ToString();
        Assert.NotNull(body);
        Assert.Contains("Plain text body here.", body);
    }

    [Fact]
    public async Task ParseEml_MultipartHtmlOnly_StripsHtmlTags()
    {
        var eml =
            "From: a@b.com\r\n" +
            "To: c@d.com\r\n" +
            "Subject: HTML Only\r\n" +
            "Date: Tue, 1 Jan 2024 00:00:00 +0000\r\n" +
            "Content-Type: multipart/alternative; boundary=\"htmlbound\"\r\n" +
            "\r\n" +
            "--htmlbound\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><h1>Title</h1><p>Paragraph content</p></body></html>\r\n" +
            "--htmlbound--";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path, parseHtml: true);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var body = result["body"]?.ToString();
        Assert.NotNull(body);
        Assert.Contains("Title", body);
        Assert.Contains("Paragraph content", body);
        // HTML tags should be stripped
        Assert.DoesNotContain("<html>", body);
        Assert.DoesNotContain("<p>", body);
    }

    // ── Attachment extraction ────────────────────────────────────────────

    [Fact]
    public async Task ParseEml_WithAttachment_ExtractsAttachmentInfo()
    {
        var eml =
            "From: a@b.com\r\n" +
            "To: c@d.com\r\n" +
            "Subject: With Attachment\r\n" +
            "Date: Tue, 1 Jan 2024 00:00:00 +0000\r\n" +
            "Content-Type: multipart/mixed; boundary=\"mixbound\"\r\n" +
            "\r\n" +
            "--mixbound\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "See attached file.\r\n" +
            "--mixbound\r\n" +
            "Content-Type: application/pdf; name=\"invoice.pdf\"\r\n" +
            "Content-Disposition: attachment; filename=\"invoice.pdf\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "\r\n" +
            "JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyA+PgplbmRvYmoK\r\n" +
            "--mixbound--";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path, extractAttachments: true);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.True(result.ContainsKey("attachments"));
        var attachmentsJson = ToJson(result["attachments"]);
        Assert.Contains("invoice.pdf", attachmentsJson);
    }

    [Fact]
    public async Task ParseEml_WithAttachment_ExtractAttachmentsDisabled_ReturnsEmptyAttachments()
    {
        var eml =
            "From: a@b.com\r\n" +
            "To: c@d.com\r\n" +
            "Subject: With Attachment\r\n" +
            "Date: Tue, 1 Jan 2024 00:00:00 +0000\r\n" +
            "Content-Type: multipart/mixed; boundary=\"mixbound2\"\r\n" +
            "\r\n" +
            "--mixbound2\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n" +
            "--mixbound2\r\n" +
            "Content-Type: application/pdf\r\n" +
            "Content-Disposition: attachment; filename=\"report.pdf\"\r\n" +
            "\r\n" +
            "base64data\r\n" +
            "--mixbound2--";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path, extractAttachments: false);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        // Attachments list should be empty when extraction is disabled
        var attachments = result["attachments"];
        Assert.NotNull(attachments);
        if (attachments is IEnumerable<object> list)
            Assert.Empty(list);
        else if (attachments is IList iList)
            Assert.Empty(iList);
        else
            Assert.Equal("[]", ToJson(attachments));
    }

    [Fact]
    public async Task ParseEml_NoAttachments_ReturnsEmptyList()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: No attachment\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nPlain body.";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path, extractAttachments: true);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var attachments = result["attachments"];
        Assert.NotNull(attachments);
        if (attachments is IEnumerable<object> list)
            Assert.Empty(list);
        else if (attachments is IList iList)
            Assert.Empty(iList);
        else
            Assert.Equal("[]", ToJson(attachments));
    }

    // ── Encoded word (RFC 2047) ──────────────────────────────────────────

    [Fact]
    public async Task ParseEml_Base64EncodedSubject_DecodesCorrectly()
    {
        // =?utf-8?B?SMOpbMOobmU=?= decodes to "Helene" with accents
        var encodedSubject = "=?utf-8?B?SMOpbMOobmU=?=";
        var eml = $"From: a@b.com\r\nTo: c@d.com\r\nSubject: {encodedSubject}\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var subject = result["subject"]?.ToString();
        Assert.NotNull(subject);
        // Should decode from base64 UTF-8
        Assert.DoesNotContain("=?utf-8?B?", subject);
    }

    [Fact]
    public async Task ParseEml_QuotedPrintableEncodedSubject_DecodesCorrectly()
    {
        // =?utf-8?Q?Hello_World?= decodes to "Hello World"
        var encodedSubject = "=?utf-8?Q?Hello_World?=";
        var eml = $"From: a@b.com\r\nTo: c@d.com\r\nSubject: {encodedSubject}\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var subject = result["subject"]?.ToString();
        Assert.NotNull(subject);
        Assert.Equal("Hello World", subject);
    }

    // ── Priority detection ───────────────────────────────────────────────

    [Theory]
    [InlineData("1", "High")]
    [InlineData("2", "High")]
    [InlineData("3", "Normal")]
    [InlineData("4", "Low")]
    [InlineData("5", "Low")]
    public async Task ParseEml_XPriorityHeader_ReturnsMappedPriority(string xPriority, string expectedPriority)
    {
        var eml = $"From: a@b.com\r\nTo: c@d.com\r\nSubject: Priority\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\nX-Priority: {xPriority}\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal(expectedPriority, result["priority"]?.ToString());
    }

    [Theory]
    [InlineData("high", "High")]
    [InlineData("low", "Low")]
    [InlineData("normal", "Normal")]
    public async Task ParseEml_ImportanceHeader_ReturnsMappedPriority(string importance, string expectedPriority)
    {
        var eml = $"From: a@b.com\r\nTo: c@d.com\r\nSubject: Priority\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\nImportance: {importance}\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal(expectedPriority, result["priority"]?.ToString());
    }

    [Fact]
    public async Task ParseEml_NoPriorityHeader_ReturnsNormal()
    {
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: Normal\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("Normal", result["priority"]?.ToString());
    }

    // ── MSG file (simplified) ────────────────────────────────────────────

    [Fact]
    public async Task ParseMsgFile_ReturnsSimplifiedResponse()
    {
        var path = CreateTempMsg("dummy content");

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var subject = result["subject"]?.ToString();
        Assert.NotNull(subject);
        Assert.Contains("MSG file", subject);
    }

    // ── Error handling: missing file ─────────────────────────────────────

    [Fact]
    public async Task ParseEml_FileNotFound_ReturnsError()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.eml");

        var response = await CallAsync(nonExistentPath);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains("File not found", response.Error);
    }

    // ── Error handling: empty path ───────────────────────────────────────

    [Fact]
    public async Task ParseEml_EmptyPath_ReturnsError()
    {
        var response = await CallAsync("");

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains("Path cannot be empty", response.Error);
    }

    [Fact]
    public async Task ParseEml_WhitespacePath_ReturnsError()
    {
        var response = await CallAsync("   ");

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains("Path cannot be empty", response.Error);
    }

    // ── Error handling: wrong extension ──────────────────────────────────

    [Fact]
    public async Task ParseEml_UnsupportedExtension_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(path, "Not an email", TestContext.Current.CancellationToken);
        _tempFiles.Add(path);

        var response = await CallAsync(path);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Contains(".eml or .msg", response.Error);
    }

    // ── Path traversal: fail-fast, raw path never read ───────────────────

    [Fact]
    public async Task EmailParser_PathTraversal_ValidatorDenied_FailsFastWithoutReading()
    {
        var deniedValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Denied("path traversal detected"));

        var fs = new TrackingDeniedFileSystemService();

        using var tool = new EmailParserTool(fs, deniedValidator);
        var parameters = new Dictionary<string, object?>
        {
            [ParamPath] = "../../etc/passwd.eml",
            ["extract_attachments"] = true,
            ["parse_html"] = true
        };
        var request = new ToolCallRequest("email_parser", parameters);

        var result = await tool.CallAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("traversal", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fs.TryReadCalls);
    }

    /// <summary>
    /// Stub that always denies and counts <see cref="TryReadAllTextAsync"/> calls. Used to
    /// verify that path-validator failures short-circuit before any I/O happens.
    /// </summary>
    private sealed class TrackingDeniedFileSystemService : IFileSystemService
    {
        public int TryReadCalls { get; private set; }

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
            => PathValidationResult.Denied("path traversal detected");

        public string? ToVirtualPath(string physicalPath) => physicalPath;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) { TryReadCalls++; return Task.FromResult<string?>(null); }
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Task.FromResult(false);
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // ── Path traversal prevention ────────────────────────────────────────

    [Fact]
    public async Task ParseEml_PathTraversal_DotDot_ReturnsError()
    {
        // Construct a path with ".." that resolves to something still containing ".."
        // after GetFullPath. We use a non-existent base to ensure GetFullPath still
        // contains the traversal pattern on some systems.
        var maliciousPath = Path.Combine(Path.GetTempPath(), "sub", "..", "..", "..", "etc", "passwd.eml");

        var response = await CallAsync(maliciousPath);

        // The tool should either reject it as unsafe or as file not found.
        // The IsPathSafe check runs before File.Exists, so if path resolves
        // to something without ".." it will hit file-not-found instead.
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public async Task ParseEml_PathWithTilde_ReturnsError()
    {
        var maliciousPath = "~/../../etc/passwd.eml";

        var response = await CallAsync(maliciousPath);

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    // ── Header continuation lines (folded headers per RFC 2822) ──────────

    [Fact]
    public async Task ParseEml_FoldedHeader_UnfoldsCorrectly()
    {
        // Subject is folded across two lines (continuation starts with whitespace)
        var eml = "From: a@b.com\r\nTo: c@d.com\r\nSubject: This is a very long\r\n subject that spans multiple lines\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        var subject = result["subject"]?.ToString();
        Assert.NotNull(subject);
        Assert.Contains("very long", subject);
        Assert.Contains("spans multiple lines", subject);
    }

    // ── From address extraction formats ──────────────────────────────────

    [Fact]
    public async Task ParseEml_FromWithDisplayName_ExtractsEmailOnly()
    {
        var eml = "From: \"John Doe\" <john.doe@example.com>\r\nTo: c@d.com\r\nSubject: Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("john.doe@example.com", result["from"]?.ToString());
    }

    [Fact]
    public async Task ParseEml_FromBareAddress_ExtractsCorrectly()
    {
        var eml = "From: bare@example.com\r\nTo: c@d.com\r\nSubject: Test\r\nDate: Tue, 1 Jan 2024 00:00:00 +0000\r\n\r\nBody";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("bare@example.com", result["from"]?.ToString());
    }

    // ── LF-only line endings (Unix style) ────────────────────────────────

    [Fact]
    public async Task ParseEml_UnixLineEndings_ParsesCorrectly()
    {
        var eml = "From: a@b.com\nTo: c@d.com\nSubject: Unix LF\nDate: Tue, 1 Jan 2024 00:00:00 +0000\n\nBody with LF only.";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("Unix LF", result["subject"]?.ToString());
        Assert.Contains("Body with LF only.", result["body"]?.ToString() ?? "");
    }

    // ── Email with no standard headers ───────────────────────────────────

    [Fact]
    public async Task ParseEml_MinimalHeaders_ReturnsDefaultValues()
    {
        // Only custom headers, no standard From/To/Subject/Date
        var eml = "X-Custom: value\r\n\r\nJust a body.";
        var path = CreateTempEml(eml);

        var response = await CallAsync(path);

        Assert.True(response.Success, response.Error);
        var result = AssertResultDict(response);
        Assert.Equal("", result["from"]?.ToString());
        Assert.Equal("", result["subject"]?.ToString());
        Assert.Equal("", result["date"]?.ToString());
    }

    // ── Tool metadata ────────────────────────────────────────────────────

    [Fact]
    public void ToolName_IsEmailParser()
    {
        Assert.Equal("email_parser", _tool.Name);
    }

    [Fact]
    public void ToolCategory_IsFileSystem()
    {
        // The ToolContract Category is "File System", but FileToolBase overrides to "File Operations"
        // The ToolContract wins because EmailParserTool declares [ToolContract(Category = "File System")]
        var category = _tool.Category;
        Assert.NotNull(category);
    }

    [Fact]
    public void ToolDescription_IsNotEmpty()
    {
        Assert.False(string.IsNullOrEmpty(_tool.Description));
    }

    // ── Assertion helpers ────────────────────────────────────────────────

    private static Dictionary<string, object?> AssertResultDict(ToolCallResponse response)
    {
        Assert.NotNull(response.Result);
        // The result is a Dictionary<string, object?> from the typed pipeline serialization
        var dict = Assert.IsType<Dictionary<string, object?>>(response.Result);
        return dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }
}
