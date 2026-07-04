using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class DirectoryReadToolTests : IDisposable
{
    private readonly DirectoryReadTool _tool;
    private readonly string _testDir;

    public DirectoryReadToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dirread_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var fs = new PassThroughFileSystemService();
        var validator = new StubPathValidator().AllowAll();

        _tool = new DirectoryReadTool(fs, validator);

        // Create test structure
        File.WriteAllText(Path.Combine(_testDir, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(_testDir, "file2.cs"), "content2");
        File.WriteAllText(Path.Combine(_testDir, "file3.txt"), "content3");
        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested");
    }

    [Fact]
    public async Task ShouldReturnDirectoryContents_WhenPathIsValid()
    {
        var request = new ToolCallRequest(
            ToolName: "directory_read",
            Parameters: new Dictionary<string, object?> { [ParamPath] = _testDir }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(3, (int)dict["total_files"]!);
        Assert.Equal(1, (int)dict["total_directories"]!);
    }

    [Fact]
    public async Task ShouldIncludeNestedFiles_WhenRecursiveIsEnabled()
    {
        var request = new ToolCallRequest(
            ToolName: "directory_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = _testDir,
                ["recursive"] = true
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(4, (int)dict["total_files"]!);
    }

    [Fact]
    public async Task ShouldFilterResults_WhenPatternIsSpecified()
    {
        var request = new ToolCallRequest(
            ToolName: "directory_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = _testDir,
                ["pattern"] = "*.txt"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var dict = result.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        Assert.Equal(2, (int)dict["total_files"]!);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathDoesNotExist()
    {
        var request = new ToolCallRequest(
            ToolName: "directory_read",
            Parameters: new Dictionary<string, object?>
            {
                [ParamPath] = "/nonexistent/path/12345"
            }
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Directory not found", result.Error);
    }

    [Fact]
    public async Task ShouldReturnError_WhenPathIsMissing()
    {
        var request = new ToolCallRequest(
            ToolName: "directory_read",
            Parameters: []
        );

        var result = await _tool.CallAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(ParamPath, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldHaveCorrectConfiguration_WhenAccessingSchema()
    {
        Assert.Equal("directory_read", _tool.Name);
        Assert.Equal("File Operations", _tool.Category);
        Assert.True(_tool.Schema.Parameters.ContainsKey(ParamPath));
        Assert.True(_tool.Schema.Parameters[ParamPath].Required);
        Assert.False(_tool.Schema.Parameters["recursive"].Required);
        Assert.False(_tool.Schema.Parameters["pattern"].Required);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_testDir, true); } catch { }
        _tool.Dispose();
    }
}
