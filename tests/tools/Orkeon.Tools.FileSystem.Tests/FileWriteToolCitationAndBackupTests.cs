using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Covers the citation-validation branch, the backup restore-on-failure path, and the
/// remaining encoding/append edge cases of <see cref="FileWriteTool"/> that are not
/// exercised by <see cref="FileWriteToolTests"/>.
/// </summary>
public sealed class FileWriteToolCitationAndBackupTests : IDisposable
{
    private readonly string _testDir;

    public FileWriteToolCitationAndBackupTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"filewrite_cite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    private string TestPath(string relativePath) => Path.Combine(_testDir, relativePath);

    private static async Task<ToolCallResponse> CallAsync(FileWriteTool tool, Dictionary<string, object?> parameters)
        => await tool.CallAsync(new ToolCallRequest("file_write", parameters));

    private const string CitationContent =
        "```csharp\n// FQN  : Foo.Bar\n// SHA  : sha256:deadbeef\npublic void Bar() {}\n```";

    // -- Citation validation branch ──────────────────────────────────────

    [Fact]
    public async Task ShouldRejectWrite_WhenCitationValidationFails()
    {
        var validator = new StubCitationValidator(
            new ValidationResult { IsValid = false, Violations = ["fabricated SHA", "missing body"] });
        using var tool = new FileWriteTool(
            new PassThroughFileSystemService(), new StubPathValidator().AllowAll(), null, validator);

        var filePath = TestPath("cited.md");
        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = CitationContent
        });

        // success:false in the typed response surfaces as an unsuccessful tool call,
        // with the violations propagated into the error message.
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        var dict = (Dictionary<string, object?>)response.Result!;
        Assert.False((bool)dict["success"]!);
        var errors = dict["errors"] as IEnumerable<object?>;
        Assert.NotNull(errors);
        Assert.Equal(2, errors!.Count());
        Assert.True(validator.WasCalled);
    }

    [Fact]
    public async Task ShouldSucceed_WhenCitationValidationPasses()
    {
        var validator = new StubCitationValidator(new ValidationResult { IsValid = true });
        using var tool = new FileWriteTool(
            new PassThroughFileSystemService(), new StubPathValidator().AllowAll(), null, validator);

        var filePath = TestPath("cited_ok.md");
        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = CitationContent
        });

        Assert.True(response.Success);
        var dict = (Dictionary<string, object?>)response.Result!;
        Assert.True((bool)dict["success"]!);
        Assert.True(validator.WasCalled);
    }

    [Fact]
    public async Task ShouldNotInvokeValidator_WhenContentHasNoCitationHeaders()
    {
        var validator = new StubCitationValidator(new ValidationResult { IsValid = false, Violations = ["should not run"] });
        using var tool = new FileWriteTool(
            new PassThroughFileSystemService(), new StubPathValidator().AllowAll(), null, validator);

        var filePath = TestPath("plain.md");
        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "Just a plain document with no FQN or SHA markers."
        });

        Assert.True(response.Success);
        var dict = (Dictionary<string, object?>)response.Result!;
        Assert.True((bool)dict["success"]!);
        Assert.False(validator.WasCalled);
    }

    [Fact]
    public async Task ShouldNotInvokeValidator_WhenOnlyFqnHeaderPresent()
    {
        var validator = new StubCitationValidator(new ValidationResult { IsValid = false, Violations = ["nope"] });
        using var tool = new FileWriteTool(
            new PassThroughFileSystemService(), new StubPathValidator().AllowAll(), null, validator);

        var filePath = TestPath("partial.md");
        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "// FQN  : Foo.Bar\nno sha header here"
        });

        Assert.True(response.Success);
        var dict = (Dictionary<string, object?>)response.Result!;
        Assert.True((bool)dict["success"]!);
        Assert.False(validator.WasCalled);
    }

    // -- Backup restore on write failure ─────────────────────────────────

    [Fact]
    public async Task ShouldRestoreBackup_WhenWriteFails()
    {
        var filePath = TestPath("restore.txt");
        const string original = "ORIGINAL CONTENT";
        await File.WriteAllTextAsync(filePath, original, TestContext.Current.CancellationToken);

        var fs = new FailingWriteFileSystem(_testDir);
        using var tool = new FileWriteTool(fs, new StubPathValidator().AllowAll());

        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "NEW CONTENT THAT WILL FAIL",
            ["create_backup"] = true
        });

        // Write fails -> tool reports failure
        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        // Original content restored from the backup
        Assert.Equal(original, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    // -- UTF-32 encoding (otherwise-unhit switch arm) ────────────────────

    [Fact]
    public async Task ShouldWriteWithUtf32Encoding()
    {
        var filePath = TestPath("utf32.txt");
        const string content = "Café UTF-32";
        using var tool = new FileWriteTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());

        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content,
            ["encoding"] = "UTF-32"
        });

        Assert.True(response.Success);
        var readBack = await File.ReadAllTextAsync(filePath, Encoding.UTF32, TestContext.Current.CancellationToken);
        Assert.Equal(content, readBack);
    }

    [Fact]
    public async Task ShouldReturnError_WhenEncodingNotInAllowedEnum()
    {
        var filePath = TestPath("unknown_enc.txt");
        using var tool = new FileWriteTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());

        var response = await CallAsync(tool, new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "fallback content",
            ["encoding"] = "EBCDIC-totally-unknown"
        });

        Assert.False(response.Success);
        Assert.Contains("encoding", response.Error);
        Assert.False(File.Exists(filePath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { /* best effort */ }
    }

    // -- Test doubles ────────────────────────────────────────────────────

    private sealed class StubCitationValidator : ICitationBlockValidator
    {
        private readonly ValidationResult _result;
        public bool WasCalled { get; private set; }

        public StubCitationValidator(ValidationResult result) => _result = result;

        public Task<ValidationResult> ValidateAsync(string fileContent, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// VFS double whose write stream throws <see cref="IOException"/> on flush/close,
    /// while supporting the backup copy/exists/delete used by the restore path.
    /// </summary>
    private sealed class FailingWriteFileSystem : IFileSystemService
    {
        private readonly PassThroughFileSystemService _inner = new();
        private readonly string _root;

        public FailingWriteFileSystem(string root) => _root = root;

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
            => PathValidationResult.Allowed(Path.GetFullPath(virtualPath));

        public string? ToVirtualPath(string physicalPath) => physicalPath;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();

        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new ThrowOnWriteStream());

        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new ThrowOnWriteStream());

        // Delegate everything else to the working pass-through implementation.
        public Task<bool> ExistsAsync(string p, CancellationToken ct) => _inner.ExistsAsync(p, ct);
        public Task CopyAsync(string s, string d, bool overwrite = false, CancellationToken ct = default) => _inner.CopyAsync(s, d, overwrite, ct);
        public Task<bool> DeleteAsync(string p, bool recursive, CancellationToken ct) => _inner.DeleteAsync(p, recursive, ct);
        public Task CreateDirectoryAsync(string p, CancellationToken ct) => _inner.CreateDirectoryAsync(p, ct);
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string r, VirtualEnumerationOptions? o, CancellationToken ct) => _inner.EnumerateFilesAsync(r, o, ct);
        public Task<Stream> OpenReadStreamAsync(string p, CancellationToken ct) => _inner.OpenReadStreamAsync(p, ct);
        public Task<byte[]?> TryReadAllBytesAsync(string p, CancellationToken ct) => _inner.TryReadAllBytesAsync(p, ct);
        public Task<string?> TryReadAllTextAsync(string p, CancellationToken ct) => _inner.TryReadAllTextAsync(p, ct);
        public Task<VirtualEntryKind> GetEntryKindAsync(string p, CancellationToken ct) => _inner.GetEntryKindAsync(p, ct);
        public Task<int> WriteAllTextAsync(string p, string c, CancellationToken ct) => _inner.WriteAllTextAsync(p, c, ct);
        public Task<int> WriteAllBytesAsync(string p, byte[] c, CancellationToken ct) => _inner.WriteAllBytesAsync(p, c, ct);
        public Task<int> AppendAllTextAsync(string p, string c, CancellationToken ct) => _inner.AppendAllTextAsync(p, c, ct);
        public Task<VirtualFileEntry?> TryGetEntryAsync(string p, CancellationToken ct) => _inner.TryGetEntryAsync(p, ct);

        private sealed class ThrowOnWriteStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() => throw new IOException("simulated disk failure");
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new IOException("simulated disk failure");
        }
    }
}
