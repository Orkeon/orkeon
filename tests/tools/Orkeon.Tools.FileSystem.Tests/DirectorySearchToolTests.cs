using System.Runtime.CompilerServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class DirectorySearchToolTests : IDisposable
{
    private readonly MockEmbeddingService _embeddingService;
    private readonly DirectorySearchTool _tool;
    private readonly string _testDir;

    public DirectorySearchToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dirsearch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _embeddingService = new MockEmbeddingService();
        _tool = new DirectorySearchTool(
            new RelativePathFileSystem(_testDir),
            EphemeralSearchHarness.Create(_embeddingService));
    }

    /// <summary>
    /// IFileSystemService stub that exposes paths under the test root as relative virtual paths
    /// while mapping back to physical for I/O. Replaces the previous Moq setup.
    /// </summary>
    private sealed class RelativePathFileSystem : IFileSystemService
    {
        private readonly string _testRoot;

        public RelativePathFileSystem(string testRoot) { _testRoot = testRoot; }

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        {
            if (virtualPath.Contains("..", StringComparison.Ordinal))
                return PathValidationResult.Denied("Path traversal ('..') is not allowed");
            return PathValidationResult.Allowed(Path.GetFullPath(virtualPath));
        }

        public string? ToVirtualPath(string physicalPath) => Path.GetRelativePath(_testRoot, physicalPath);

        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();

        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        {
            var physical = Path.IsPathRooted(virtualPath) ? virtualPath : Path.Combine(_testRoot, virtualPath);
            return Task.FromResult(Directory.Exists(physical) || File.Exists(physical));
        }

        public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
            string virtualRoot,
            VirtualEnumerationOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var physRoot = Path.IsPathRooted(virtualRoot) ? virtualRoot : Path.Combine(_testRoot, virtualRoot);
            var searchOption = (options?.Recursive ?? true) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var pattern = options?.SearchPattern ?? "*";
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(physRoot, pattern, searchOption); }
            catch { yield break; }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                yield return new VirtualFileEntry(
                    VirtualPath: Path.GetRelativePath(_testRoot, f),
                    SizeBytes: new FileInfo(f).Length,
                    LastModified: DateTimeOffset.UtcNow,
                    Kind: VirtualEntryKind.File);
            }
            await Task.CompletedTask;
        }

        public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
        {
            var physical = Path.IsPathRooted(virtualPath) ? virtualPath : Path.Combine(_testRoot, virtualPath);
            return File.Exists(physical) ? await File.ReadAllTextAsync(physical, ct) : null;
        }

        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
        {
            var physical = Path.IsPathRooted(virtualPath) ? virtualPath : Path.Combine(_testRoot, virtualPath);
            return Task.FromResult<Stream>(File.OpenRead(physical));
        }

        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => throw new NotImplementedException();
    }

    // -- Helper methods ──────────────────────────────────────────────────

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
    }

    private void CreateBinaryFile(string relativePath)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        // Write bytes with null byte in the first 8KB
        var data = new byte[1024];
        data[0] = 0x89; // PNG-like header
        data[1] = 0x50;
        data[10] = 0x00; // Null byte => binary
        File.WriteAllBytes(fullPath, data);
    }

    private async Task<ToolCallResponse> CallToolAsync(Dictionary<string, object?> parameters)
    {
        var request = new ToolCallRequest(
            ToolName: "directory_search",
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

    // -- Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReturnResults_WhenQueryMatchesFileContent()
    {
        // Arrange: use a factory that makes the query and matching content very similar
        _embeddingService.SetEmbeddingFactory(text =>
        {
            if (text.Contains("error handling") || text.Contains("try catch"))
                return [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
            return [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f]; // orthogonal
        });

        CreateFile("handler.cs", "This file explains try catch error handling patterns.\n\nIt covers multiple scenarios.");
        CreateFile("readme.txt", "This is an unrelated readme file.\n\nNothing about errors here.");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "error handling",
            ["threshold"] = 0.5
        });

        var dict = ResultDict(result);
        Assert.True((int)dict["result_count"]! > 0);

        var results = dict["results"] as IEnumerable<object>;
        Assert.NotNull(results);
        var first = results.Cast<Dictionary<string, object?>>().First();
        Assert.Contains("try catch", first["content"]!.ToString()!);
    }

    [Fact]
    public async Task ShouldSearchRecursively_WhenRecursiveIsTrue()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("top.txt", "Top level content about algorithms.");
        CreateFile("sub/nested.txt", "Nested content about algorithms.");
        CreateFile("sub/deep/deep.txt", "Deep nested content about algorithms.");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "algorithms",
            ["recursive"] = true,
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.True((int)dict["files_processed"]! >= 3);
    }

    [Fact]
    public async Task ShouldNotSearchSubdirs_WhenRecursiveIsFalse()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("top.txt", "Top level content.\n\nSome paragraph.");
        CreateFile("sub/nested.txt", "Nested content.\n\nAnother paragraph.");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "content",
            ["recursive"] = false,
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.Equal(1, (int)dict["files_processed"]!);
    }

    [Fact]
    public async Task ShouldFilterByPattern_WhenFilePatternsSet()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("code.cs", "C# code with patterns.\n\nMore code.");
        CreateFile("notes.txt", "Text notes with patterns.\n\nMore notes.");
        CreateFile("data.json", "JSON data content.\n\nMore data.");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "patterns",
            ["file_patterns"] = "*.cs;*.txt",
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.Equal(2, (int)dict["files_processed"]!);
    }

    [Fact]
    public async Task ShouldSkipLargeFiles_WhenExceedingMaxFileSize()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("small.txt", "Small file content.\n\nAnother paragraph.");

        // Create a file > 1 KB
        var largeContent = new string('x', 2048);
        CreateFile("large.txt", largeContent);

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "content",
            ["max_file_size_kb"] = 1, // 1 KB limit
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.Equal(1, (int)dict["files_processed"]!);
        Assert.Equal(1, (int)dict["files_skipped"]!);
    }

    [Fact]
    public async Task ShouldRespectTopK_WhenManyChunksMatch()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        // Create a file with many paragraphs
        var paragraphs = Enumerable.Range(1, 20)
            .Select(i => $"Paragraph number {i} with interesting content about algorithms and data structures.")
            .ToArray();
        CreateFile("multi.txt", string.Join("\n\n", paragraphs));

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "algorithms",
            ["top_k"] = 3,
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.Equal(3, (int)dict["result_count"]!);
        Assert.True((int)dict["total_chunks"]! > 3);
    }

    [Fact]
    public async Task ShouldReturnError_WhenDirectoryDoesNotExist()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = Path.Combine(_testDir, "nonexistent_subdir"),
            [ParamQuery] = "anything"
        });

        Assert.False(result.Success);
        Assert.Contains("Directory not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenQueryIsEmpty()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = ""
        });

        Assert.False(result.Success);
        Assert.Contains("Query cannot be empty", result.Error);
    }

    [Fact]
    public async Task ShouldReturnRelativePaths_InResults()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("sub/deep/code.cs", "Some code content here.\n\nAnother paragraph.");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "code",
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        var results = dict["results"] as IEnumerable<object>;
        Assert.NotNull(results);

        foreach (var item in results.Cast<Dictionary<string, object?>>())
        {
            var sourcePath = item["source_file"]!.ToString()!;
            // Should be relative (not start with /)
            Assert.False(Path.IsPathRooted(sourcePath), $"Path should be relative but was: {sourcePath}");
            Assert.Contains("sub", sourcePath);
            Assert.Contains("code.cs", sourcePath);
        }
    }

    [Fact]
    public async Task ShouldSkipBinaryFiles()
    {
        _embeddingService.SetEmbeddingFactory(_ => [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]);

        CreateFile("text.txt", "Regular text content.\n\nAnother paragraph.");
        CreateBinaryFile("image.bin");

        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            [ParamQuery] = "content",
            ["threshold"] = 0.0
        });

        var dict = ResultDict(result);
        Assert.Equal(1, (int)dict["files_processed"]!);
        Assert.Equal(1, (int)dict["files_skipped"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathContainsTraversal()
    {
        var result = await CallToolAsync(new Dictionary<string, object?>
        {
            [ParamPath] = Path.Combine(_testDir, "../../etc"),
            [ParamQuery] = "secret"
        });

        Assert.False(result.Success);
        Assert.Contains("..", result.Error);
    }

    [Fact]
    public void ShouldHaveCorrectSchemaConfiguration()
    {
        Assert.Equal("directory_search", _tool.Name);
        Assert.NotNull(_tool.Schema);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamPath));
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamQuery));
        Assert.True(_tool.Schema.Parameters[ParamQuery].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey("file_patterns"));
        Assert.False(_tool.Schema.Parameters["file_patterns"].Required);
        Assert.True(_tool.Schema.Parameters.ContainsKey("top_k"));
        Assert.False(_tool.Schema.Parameters["top_k"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
