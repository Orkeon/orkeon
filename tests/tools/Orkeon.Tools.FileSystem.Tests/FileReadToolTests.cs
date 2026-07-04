using System.Text;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class FileReadToolTests : IDisposable
{
    private readonly FileReadTool _tool;
    private readonly string _testDir;

    public FileReadToolTests()
    {
        _tool = new FileReadTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
        _testDir = Path.Combine(Path.GetTempPath(), $"fileread_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    // -- Helper methods ──────────────────────────────────────────────────

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string CreateFile(string relativePath, string content, Encoding encoding)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content, encoding);
        return fullPath;
    }

    private async Task<ToolCallResponse> CallToolAsync(Dictionary<string, object?> parameters)
    {
        var request = new ToolCallRequest(
            ToolName: "file_read",
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

    // -- Happy path tests ────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReadTextFile_WhenPathIsValid()
    {
        var content = "Hello, World!\nSecond line.";
        var filePath = CreateFile("simple.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
        Assert.Equal(false, dict["truncated"]);
    }

    [Fact]
    public async Task ShouldReturnFileInfo_WhenReadSucceeds()
    {
        var content = "Some file content for metadata test.";
        var filePath = CreateFile("metadata.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        var dict = ResultDict(result);
        var fileInfo = dict["file_info"] as Dictionary<string, object?>;
        Assert.NotNull(fileInfo);
        Assert.Equal(".txt", fileInfo["extension"]?.ToString());
        Assert.True(Convert.ToInt64(fileInfo["size"]) > 0);
        Assert.Contains("UTC", fileInfo["last_modified"]?.ToString());
        Assert.Contains("UTC", fileInfo["created"]?.ToString());
        Assert.Equal(Path.GetFullPath(filePath), fileInfo["full_path"]?.ToString());
    }

    [Fact]
    public async Task ShouldReadEmptyFile_ReturningEmptyContent()
    {
        var filePath = CreateFile("empty.txt", string.Empty);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        var dict = ResultDict(result);
        Assert.Equal(string.Empty, dict["content"]?.ToString());
        Assert.Equal(false, dict["truncated"]);
    }

    [Fact]
    public async Task ShouldReadJsonFile_WhenPathIsValid()
    {
        var jsonContent = """{"name":"test","value":42}""";
        var filePath = CreateFile("data.json", jsonContent);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        var dict = ResultDict(result);
        Assert.Equal(jsonContent, dict["content"]?.ToString());
    }

    [Fact]
    public async Task ShouldReadLargeFile_Successfully()
    {
        // Build a large file (~100 KB)
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
            sb.AppendLine($"Line {i}: {new string('x', 100)}");
        var content = sb.ToString();
        var filePath = CreateFile("large.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
        Assert.Equal(false, dict["truncated"]);
    }

    // -- Encoding tests ──────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReadUtf8File_WithExplicitEncoding()
    {
        var content = "Caf\u00e9 \u00e9l\u00e8ve r\u00e9sum\u00e9";
        var filePath = CreateFile("utf8.txt", content, Encoding.UTF8);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["encoding"] = "UTF-8"
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
    }

    [Fact]
    public async Task ShouldReadAsciiFile_WithAsciiEncoding()
    {
        var content = "Plain ASCII text 12345";
        var filePath = CreateFile("ascii.txt", content, Encoding.ASCII);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["encoding"] = "ASCII"
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
    }

    [Fact]
    public async Task ShouldReadUnicodeFile_WithUnicodeEncoding()
    {
        var content = "Unicode content: \u4f60\u597d\u4e16\u754c";
        var filePath = CreateFile("unicode.txt", content, Encoding.Unicode);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["encoding"] = "Unicode"
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
    }

    [Fact]
    public async Task ShouldDefaultToUtf8_WhenEncodingNotSpecified()
    {
        var content = "Default encoding test with accents: \u00e9\u00e0\u00fc";
        var filePath = CreateFile("default_enc.txt", content, Encoding.UTF8);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
            // No encoding specified -- should default to UTF-8
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
    }

    [Fact]
    public async Task ShouldRejectUnknownEncoding_WhenEnumConstraintViolated()
    {
        var content = "Fallback encoding content";
        var filePath = CreateFile("unknown_enc.txt", content, Encoding.UTF8);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["encoding"] = "SOME-UNKNOWN-ENCODING"
        });

        // The schema defines an enum for encoding; invalid values are rejected at validation
        Assert.False(result.Success);
        Assert.Contains("encoding", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // -- MaxLength / truncation tests ────────────────────────────────────

    [Fact]
    public async Task ShouldTruncateContent_WhenMaxLengthExceeded()
    {
        var content = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var filePath = CreateFile("truncate.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["max_length"] = 10
        });

        var dict = ResultDict(result);
        Assert.Equal("ABCDEFGHIJ", dict["content"]?.ToString());
        Assert.Equal(true, dict["truncated"]);
    }

    [Fact]
    public async Task ShouldNotTruncate_WhenContentShorterThanMaxLength()
    {
        var content = "Short";
        var filePath = CreateFile("short.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath,
            ["max_length"] = 1000
        });

        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
        Assert.Equal(false, dict["truncated"]);
    }

    [Fact]
    public async Task ShouldReturnFileInfo_WithSizeAndContent_WhenReadSucceeds()
    {
        var content = "Metadata test content";
        var filePath = CreateFile("meta_bytes.txt", content);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = filePath
        });

        Assert.True(result.Success);
        var dict = ResultDict(result);
        Assert.Equal(content, dict["content"]?.ToString());
        var fileInfo = dict["file_info"] as Dictionary<string, object?>;
        Assert.NotNull(fileInfo);
        Assert.True(Convert.ToInt64(fileInfo["size"]) > 0);
    }

    // -- Error handling tests ────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnError_WhenFileDoesNotExist()
    {
        var missingPath = Path.Combine(_testDir, "does_not_exist.txt");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = missingPath
        });

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathIsEmpty()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = ""
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathParameterIsMissing()
    {
        var result = await CallToolAsync([]);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // -- Path traversal prevention tests ─────────────────────────────────

    [Fact]
    public async Task ShouldPreventPathTraversal_WithDotDot()
    {
        // Without an IPathValidator, the tool uses the legacy IsPathSafe fallback.
        // Path.GetFullPath resolves ".." but IsPathSafe checks for ".." in the resolved path.
        // With the default constructor (no IPathValidator), paths that resolve outside
        // the temp dir still resolve successfully via GetFullPath. We need to use
        // a path validator for real traversal prevention.

        var traversalValidator = new TraversalBlockingPathValidator(_testDir);
        var fs = new PassThroughFileSystemService((path, _) => traversalValidator.ValidatePath(path));
        using var secureTool = new FileReadTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = Path.Combine(_testDir, "../../etc/passwd")
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
        using var secureTool = new FileReadTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/etc/passwd"
            }
        ), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ShouldAllowValidPath_WhenPathValidatorAccepts()
    {
        var content = "Allowed content";
        var filePath = CreateFile("allowed.txt", content);

        var traversalValidator = new TraversalBlockingPathValidator(_testDir);
        var fs = new PassThroughFileSystemService((path, _) => traversalValidator.ValidatePath(path));
        using var secureTool = new FileReadTool(fs, traversalValidator);

        var result = await secureTool.CallAsync(new ToolCallRequest(
            ToolName: "file_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = filePath
            }
        ), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error ?? "Expected success");
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(content, dict["content"]?.ToString());
    }

    // -- Schema / configuration tests ────────────────────────────────────

    [Fact]
    public void ShouldHaveCorrectName()
    {
        Assert.Equal("file_read", _tool.Name);
    }

    [Fact]
    public void ShouldHaveCorrectCategory()
    {
        Assert.Equal("File Operations", _tool.Category);
    }

    [Fact]
    public void ShouldHaveSchemaWithRequiredPathParameter()
    {
        Assert.NotNull(_tool.Schema);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamPath));
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
    }

    [Fact]
    public void ShouldHaveSchemaWithOptionalEncodingParameter()
    {
        Assert.True(_tool.Schema.Parameters.ContainsKey("encoding"));
        Assert.False(_tool.Schema.Parameters["encoding"].Required);
    }

    [Fact]
    public void ShouldHaveSchemaWithOptionalMaxLengthParameter()
    {
        Assert.True(_tool.Schema.Parameters.ContainsKey("max_length"));
        Assert.False(_tool.Schema.Parameters["max_length"].Required);
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
