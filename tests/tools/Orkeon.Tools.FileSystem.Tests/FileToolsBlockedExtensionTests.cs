using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Issue #215 through the tools, with the production VFS and path validator: <c>file_read</c>
/// reads a shell script, <c>file_write</c> never writes one.
/// </summary>
public sealed class FileToolsBlockedExtensionTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileSystemRegistry _registry;

    public FileToolsBlockedExtensionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"blocked_ext_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        File.WriteAllText(Path.Combine(_testDir, "run.sh"), "#!/usr/bin/env bash\necho hi\n");
        _registry = new FileSystemRegistry([new FileSystemMount(_testDir, "/workspace", FileAccessRights.ReadWrite)]);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private (FileSystemService Fs, PathValidator Validator) Build(bool blockReads = false)
    {
        var options = new PathSecurityOptions { DefaultWorkspaceRoot = _testDir, BlockReadsOfBlockedExtensions = blockReads };
        var validator = new PathValidator(options, NullLogger<PathValidator>.Instance);
        return (new FileSystemService(_registry, validator, NullLogger<FileSystemService>.Instance), validator);
    }

    [Fact]
    public async Task ShouldReadScript_WhenFileReadTargetsBlockedExtension()
    {
        var (fs, validator) = Build();
        using var tool = new FileReadTool(fs, validator);

        var response = await tool.CallAsync(new ToolCallRequest("file_read",
            new Dictionary<string, object?> { [ParamPath] = "/workspace/run.sh" }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var dict = Assert.IsType<Dictionary<string, object?>>(response.Result);
        Assert.Contains("echo hi", dict["content"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseRead_WhenOptionBlocksReadsOfBlockedExtensions()
    {
        var (fs, validator) = Build(blockReads: true);
        using var tool = new FileReadTool(fs, validator);

        var response = await tool.CallAsync(new ToolCallRequest("file_read",
            new Dictionary<string, object?> { [ParamPath] = "/workspace/run.sh" }), TestContext.Current.CancellationToken);

        Assert.False(IsSuccessful(response));
    }

    [Fact]
    public async Task ShouldRefuseWrite_WhenFileWriteTargetsBlockedExtension()
    {
        var (fs, validator) = Build();
        using var tool = new FileWriteTool(fs, validator);

        var response = await tool.CallAsync(new ToolCallRequest("file_write",
            new Dictionary<string, object?> { [ParamPath] = "/workspace/run.sh", ["content"] = "rm -rf /" }),
            TestContext.Current.CancellationToken);

        Assert.False(IsSuccessful(response));
        Assert.Contains("echo hi", await File.ReadAllTextAsync(Path.Combine(_testDir, "run.sh"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static bool IsSuccessful(ToolCallResponse response)
        => response.Success
           && !(response.Result is Dictionary<string, object?> dict
                && dict.TryGetValue("success", out var ok) && ok is false);
}
