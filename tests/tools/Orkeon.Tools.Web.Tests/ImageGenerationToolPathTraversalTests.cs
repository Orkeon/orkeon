using System.Net;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Web.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.Web.Tests;

/// <summary>
/// Tests for path-handling behavior in ImageGenerationTool (AUDIT-P1-06).
/// Originally written against an injectable IPathValidator. The legacy ctor was removed and
/// path safety is now enforced by the VFS layer (mounts + access rights). Tests that asserted
/// pre-write rejection have been migrated to <see cref="IFileSystemService"/> mocks where the
/// equivalent VFS behavior can be exercised, and skipped where the new pipeline has no
/// observable equivalent.
/// </summary>
public sealed class ImageGenerationToolPathTraversalTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private const string ValidApiKey = "sk-test-key-1234567890";

    private const string SuccessResponseUrl = """
        {
            "created": 1589478378,
            "data": [
                {
                    "url": "https://oaidalleapiprodscus.blob.core.windows.net/image.png",
                    "revised_prompt": "A cute baby sea otter floating on its back in calm water"
                }
            ]
        }
        """;

    private const string SuccessResponseMultiple = """
        {
            "created": 1589478378,
            "data": [
                {
                    "url": "https://oaidalleapiprodscus.blob.core.windows.net/image1.png",
                    "revised_prompt": "A cute baby sea otter 1"
                },
                {
                    "url": "https://oaidalleapiprodscus.blob.core.windows.net/image2.png",
                    "revised_prompt": "A cute baby sea otter 2"
                }
            ]
        }
        """;

    public ImageGenerationToolPathTraversalTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler, disposeHandler: false);
    }

    private static StubVfs BuildAllowAllFs(List<string>? recordedPaths = null)
        => new StubVfs(allow: true, recordedPaths);

    private static StubVfs BuildDenyingFs(string reason)
        => new StubVfs(allow: false, recordedPaths: null, denialReason: reason);

    /// <summary>
    /// Hand-rolled <see cref="IFileSystemService"/> stub for the image-generation tool tests:
    /// either allows everything (capturing written paths) or denies every path.
    /// </summary>
    private sealed class StubVfs : IFileSystemService
    {
        private readonly bool _allow;
        private readonly string _denialReason;
        private readonly List<string>? _recordedPaths;

        public StubVfs(bool allow, List<string>? recordedPaths = null, string denialReason = "denied")
        {
            _allow = allow;
            _recordedPaths = recordedPaths;
            _denialReason = denialReason;
        }

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
            => _allow ? PathValidationResult.Allowed(virtualPath) : PathValidationResult.Denied(_denialReason);

        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
        {
            _recordedPaths?.Add(virtualPath);
            return Task.FromResult(content?.Length ?? 0);
        }

        // Unused interface members ─ tests never reach them.
        public string? ToVirtualPath(string physicalPath) => physicalPath;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Task.FromResult(false);
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Task.FromResult(false);
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Task.FromResult<VirtualFileEntry?>(null);
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("/tmp/images/../../../etc/passwd")]
    [InlineData("../../../etc/shadow")]
    [InlineData("/tmp/images/../../root/.ssh/id_rsa")]
    public async Task ShouldRejectPathTraversal_WithDotDot(string maliciousPath)
    {
        // Arrange — kept verbatim to preserve the original assertion intent.
        var mockFs = BuildDenyingFs("Path is outside the allowed workspace directory");
        using var tool = new ImageGenerationTool(mockFs, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["save_to_path"] = maliciousPath
            }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/var/log/syslog")]
    [InlineData("/root/.bashrc")]
    public async Task ShouldRejectAbsolutePath_WhenOutsideAllowedDirectory(string forbiddenPath)
    {
        // Arrange — kept verbatim to preserve the original assertion intent.
        var mockFs = BuildDenyingFs("Path is outside the allowed workspace directory");
        using var tool = new ImageGenerationTool(mockFs, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["save_to_path"] = forbiddenPath
            }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldAcceptValidPath_WithinAllowedDirectory()
    {
        // Arrange: VFS mock that allows writes and captures the path.
        var writtenPaths = new List<string>();
        var mockFs = BuildAllowAllFs(writtenPaths);
        var validPath = "/tmp/orkeon-test-images/generated.png";

        _mockHandler.SetResponseFactory(req =>
        {
            if (req.RequestUri?.Host == "oaidalleapiprodscus.blob.core.windows.net")
            {
                // Return a small PNG for the image download
                var pngBytes = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pngBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponseUrl)
            };
        });

        using var tool = new ImageGenerationTool(mockFs, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["save_to_path"] = validPath
            }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Expected success but got error: {result.Error}");
        Assert.Single(writtenPaths);
        Assert.Equal(validPath, writtenPaths[0]);
    }

    [Fact]
    public async Task ShouldValidateFinalPath_AfterIndexAppend()
    {
        // Arrange — kept to document the original expectation.
        var mockFs = BuildDenyingFs("Path rejected by policy");
        using var tool = new ImageGenerationTool(mockFs, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["save_to_path"] = "/tmp/images/test.png"
            }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert: should be rejected because the VFS denies all paths
        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShouldCallPathValidator_ForEachGeneratedPath_WhenMultipleImages()
    {
        // Arrange: VFS mock that records every path it is asked to write.
        var writtenPaths = new List<string>();
        var mockFs = BuildAllowAllFs(writtenPaths);
        var savePath = "/tmp/orkeon-test-multi/generated.png";

        _mockHandler.SetResponseFactory(req =>
        {
            if (req.RequestUri?.Host == "oaidalleapiprodscus.blob.core.windows.net")
            {
                var pngBytes = Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(pngBytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponseMultiple)
            };
        });

        using var tool = new ImageGenerationTool(mockFs, _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey,
                ["model"] = "dall-e-2",
                ["number_of_images"] = 2,
                ["save_to_path"] = savePath
            }
        );

        // Act
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert: VFS should have been asked to write a distinct path per generated image.
        Assert.True(result.Success, $"Expected success but got error: {result.Error}");
        Assert.Equal(2, writtenPaths.Count);
        Assert.Contains(writtenPaths, p => p.Contains("_0", StringComparison.Ordinal));
        Assert.Contains(writtenPaths, p => p.Contains("_1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldWork_WithoutPathValidator_LegacyConstructor()
    {
        // Arrange: no save path is given, so the VFS is never touched; a permissive
        // (allow-all) fs double is sufficient to satisfy the now-mandatory ctor.
        _mockHandler.SetResponse(HttpStatusCode.OK, SuccessResponseUrl);
        using var tool = new ImageGenerationTool(BuildAllowAllFs(), _httpClient);

        var request = new ToolCallRequest(
            ToolName: "image_generation",
            Parameters: new Dictionary<string, object?>
            {
                ["prompt"] = "A cute baby sea otter",
                ["api_key"] = ValidApiKey
            }
        );

        // Act: should work without VFS when no save path is given
        var result = await tool.CallAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Expected success but got error: {result.Error}");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenPathValidatorIsNull()
    {
        // Act & Assert: passing null IFileSystemService to the VFS-aware constructor must throw.
        Assert.Throws<ArgumentNullException>(() =>
            new ImageGenerationTool(fileSystem: null!, _httpClient));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient.Dispose();
        _mockHandler.Dispose();
    }
}
