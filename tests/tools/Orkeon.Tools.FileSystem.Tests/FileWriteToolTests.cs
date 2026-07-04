using System.Text;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class FileWriteToolTests : IDisposable
{
    private readonly FileWriteTool _tool;
    private readonly string _testDir;

    public FileWriteToolTests()
    {
        _tool = new FileWriteTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"filewrite_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    // -- Helper methods ──────────────────────────────────────────────────

    private async Task<ToolCallResponse> CallToolAsync(Dictionary<string, object?> parameters)
    {
        var request = new ToolCallRequest(
            ToolName: "file_write",
            Parameters: parameters
        );
        return await _tool.CallAsync(request);
    }

    private static Dictionary<string, object?> ResultDict(ToolCallResponse response)
    {
        Assert.True(response.Success, response.Error ?? "Expected success");
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict;
    }

    private string TestPath(string relativePath) => Path.Combine(_testDir, relativePath);

    // -- Happy path tests ────────────────────────────────────────────────

    [Fact]
    public async Task ShouldWriteNewFile_WhenPathIsValid()
    {
        var filePath = TestPath("new_file.txt");
        var content = "Hello, World!";

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.Equal(Path.GetFullPath(filePath), dict[ParamPath]?.ToString());
        Assert.True((int)dict["bytes_written"]! > 0);

        // Verify actual file content
        var written = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        Assert.Equal(content, written);
    }

    [Fact]
    public async Task ShouldCreateNestedDirectories_WhenParentDoesNotExist()
    {
        var filePath = TestPath(Path.Combine("level1", "level2", "level3", "nested.txt"));
        var content = "Deeply nested content";

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldOverwriteExistingFile_WhenAppendIsFalse()
    {
        var filePath = TestPath("overwrite.txt");
        await File.WriteAllTextAsync(filePath, "Original content", TestContext.Current.CancellationToken);

        var newContent = "Overwritten content";
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = newContent
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.Equal(newContent, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldAppendToExistingFile_WhenAppendIsTrue()
    {
        var filePath = TestPath("append.txt");
        var original = "First part.";
        await File.WriteAllTextAsync(filePath, original, TestContext.Current.CancellationToken);

        var appended = " Second part.";
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = appended,
            ["append"] = true
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.Equal(original + appended, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldWriteEmptyContent_Successfully()
    {
        var filePath = TestPath("empty.txt");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = ""
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.True(File.Exists(filePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
        Assert.Equal(0, (int)dict["bytes_written"]!);
    }

    // -- Encoding tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ShouldWriteWithAsciiEncoding()
    {
        var filePath = TestPath("ascii.txt");
        var content = "Plain ASCII 12345";

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content,
            ["encoding"] = "ASCII"
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);

        var bytes = await File.ReadAllBytesAsync(filePath, TestContext.Current.CancellationToken);
        var readBack = Encoding.ASCII.GetString(bytes);
        Assert.Equal(content, readBack);
    }

    [Fact]
    public async Task ShouldWriteWithUnicodeEncoding()
    {
        var filePath = TestPath("unicode.txt");
        var content = "Unicode content: \u4f60\u597d\u4e16\u754c";

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content,
            ["encoding"] = "Unicode"
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);

        var readBack = await File.ReadAllTextAsync(filePath, Encoding.Unicode, TestContext.Current.CancellationToken);
        Assert.Equal(content, readBack);
    }

    [Fact]
    public async Task ShouldDefaultToUtf8_WhenEncodingNotSpecified()
    {
        var filePath = TestPath("default_enc.txt");
        var content = "Caf\u00e9 \u00e9l\u00e8ve r\u00e9sum\u00e9";

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);

        var readBack = await File.ReadAllTextAsync(filePath, Encoding.UTF8, TestContext.Current.CancellationToken);
        Assert.Equal(content, readBack);
    }

    // -- Backup tests ────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldCreateBackup_WhenCreateBackupIsTrue()
    {
        var filePath = TestPath("backup_target.txt");
        var originalContent = "Original content for backup";
        await File.WriteAllTextAsync(filePath, originalContent, TestContext.Current.CancellationToken);

        var newContent = "New content after backup";
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = newContent,
            ["create_backup"] = true
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);

        // Verify backup was created
        var backupPath = dict["backup_path"]?.ToString();
        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath), $"Backup file should exist at {backupPath}");

        // Verify backup contains original content
        var backupContent = await File.ReadAllTextAsync(backupPath!, TestContext.Current.CancellationToken);
        Assert.Equal(originalContent, backupContent);

        // Verify main file has new content
        Assert.Equal(newContent, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldNotCreateBackup_WhenFileDoesNotExist()
    {
        var filePath = TestPath("no_backup_needed.txt");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "Fresh file content",
            ["create_backup"] = true
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        // backup_path may be absent from dict when null (not serialized)
        if (dict.TryGetValue("backup_path", out var backupValue))
            Assert.Null(backupValue);
    }

    // -- Error handling tests ────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenPathIsEmpty()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = "",
            ["content"] = "some content"
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathParameterIsMissing()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            ["content"] = "some content"
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // -- Path traversal prevention tests ─────────────────────────────────

    [Fact]
    public async Task ShouldPreventPathTraversal_WithDotDot()
    {
        var traversalValidator = new TraversalBlockingPathValidator(_testDir);
        var fs = new PassThroughFileSystemService((path, _) => traversalValidator.ValidatePath(path));
        using var secureTool = new FileWriteTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_write",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = Path.Combine(_testDir, "../../etc/evil.txt"),
                ["content"] = "malicious"
            }
        ), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldPreventPathTraversal_WithAbsoluteSystemPath()
    {
        var traversalValidator = new TraversalBlockingPathValidator(_testDir);
        var fs = new PassThroughFileSystemService((path, _) => traversalValidator.ValidatePath(path));
        using var secureTool = new FileWriteTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_write",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/etc/evil.txt",
                ["content"] = "malicious"
            }
        ), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldAllowValidPath_WhenPathValidatorAccepts()
    {
        var content = "Allowed write";
        var filePath = TestPath("allowed_write.txt");

        var traversalValidator = new TraversalBlockingPathValidator(_testDir);
        var fs = new PassThroughFileSystemService((path, _) => traversalValidator.ValidatePath(path));
        using var secureTool = new FileWriteTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_write",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath,
                ["content"] = content
            }
        ), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "Expected success");
        Assert.Equal(content, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    // -- Large file test ─────────────────────────────────────────────────

    [Fact]
    public async Task ShouldWriteLargeFile_Successfully()
    {
        var filePath = TestPath("large.txt");
        // Build a ~1 MB string
        var sb = new StringBuilder();
        for (int i = 0; i < 10_000; i++)
            sb.AppendLine($"Line {i}: {new string('A', 100)}");
        var content = sb.ToString();

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = content
        });

        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.True((int)dict["bytes_written"]! > 1_000_000);
        Assert.Equal(content, await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    // -- Metadata tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ShouldWriteSuccessfully_ForOverwrite()
    {
        var filePath = TestPath("meta_overwrite.txt");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = "test"
        });

        Assert.True(result.Success);
        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.True(File.Exists(filePath));
        Assert.Equal("test", await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldWriteSuccessfully_ForAppend()
    {
        var filePath = TestPath("meta_append.txt");
        await File.WriteAllTextAsync(filePath, "base", TestContext.Current.CancellationToken);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["content"] = " appended",
            ["append"] = true
        });

        Assert.True(result.Success);
        var dict = ResultDict(result);
        Assert.True((bool)dict["success"]!);
        Assert.Equal("base appended", await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken));
    }

    // -- Schema / tool identity tests ────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectName()
    {
        Assert.Equal("file_write", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("File Operations", _tool.Category);
    }

    [Fact]
    public void ShouldHaveSchemaWithRequiredParameters()
    {
        Assert.NotNull(_tool.Schema);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamPath));
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey("content"));
        Assert.True(_tool.Schema.Parameters["content"].Required);
    }

    [Fact]
    public void ShouldHaveSchemaWithOptionalParameters()
    {
        Assert.True(_tool.Schema.Parameters.ContainsKey("encoding"));
        Assert.False(_tool.Schema.Parameters["encoding"].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey("append"));
        Assert.False(_tool.Schema.Parameters["append"].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey("create_backup"));
        Assert.False(_tool.Schema.Parameters["create_backup"].Required);
    }

    // -- Cleanup ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }

    // -- Test double ─────────────────────────────────────────────────────

    /// <summary>
    /// Simple IPathValidator test double that restricts paths to a given root directory.
    /// </summary>
    private sealed class TraversalBlockingPathValidator : IPathValidator
    {
        private readonly string _allowedRoot;

        public TraversalBlockingPathValidator(string allowedRoot)
        {
            _allowedRoot = Path.GetFullPath(allowedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                return PathValidationResult.Denied("Path cannot be null or empty");

            try
            {
                var resolved = Path.GetFullPath(requestedPath);
                var normalizedRoot = _allowedRoot + Path.DirectorySeparatorChar;

                if (resolved.StartsWith(normalizedRoot, StringComparison.Ordinal) ||
                    resolved.Equals(_allowedRoot, StringComparison.Ordinal))
                {
                    return PathValidationResult.Allowed(resolved);
                }

                return PathValidationResult.Denied(
                    $"Path '{resolved}' is outside the allowed root '{_allowedRoot}'");
            }
            catch (Exception ex)
            {
                return PathValidationResult.Denied($"Invalid path: {ex.Message}");
            }
        }
    }
}
