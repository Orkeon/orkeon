using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;
using Orkeon.Tests.Shared.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// #349 through a real tool call and the real file system: glob, grep and a recursive directory
/// listing under an agent's file-system scope never return a file the scope does not let it read,
/// whether the search starts above the scope or inside it, over a sub-entry that grants nothing.
/// </summary>
public sealed class FileSearchToolsAgentScopeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemRegistry _registry;
    private readonly FileSystemService _service;

    public FileSearchToolsAgentScopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-search-scope-{Guid.NewGuid():N}");
        Write("repo/open.txt", "needle in the open");
        Write("repo/secret/key.txt", "needle in the key");
        Write("other/secret.txt", "needle outside");
        _registry = new FileSystemRegistry([new FileSystemMount(_tempDir, "/workspace", FileAccessRights.ReadWrite,
            [new SubPathOverride("repo/secret", FileAccessRights.None)])]);
        var validator = new PathValidator(new PathSecurityOptions { DefaultWorkspaceRoot = _tempDir }, NullLogger<PathValidator>.Instance);
        _service = new FileSystemService(_registry, validator, NullLogger<FileSystemService>.Instance);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_tempDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// The repo is readable, its <c>secret</c> folder grants nothing (a mount override: a scope entry
    /// cannot grant nothing), the rest of the mount is outside.
    /// </summary>
    private AgentFileSystemScope Scope()
        => AgentFileSystemScope.Resolve("reader", [new FileSystemScopeEntry("/workspace/repo")], _service.GetAvailableMounts());

    public static TheoryData<string, string> Searches() => new()
    {
        { "glob", "/workspace" },
        { "grep", "/workspace" },
        { "grep", "/workspace/repo" },
        { "directory_read", "/workspace" },
    };

    /// <summary>A listing that starts inside the scope, over a sub-entry that grants nothing.</summary>
    public static TheoryData<string, string> ListingsOverAWithheldSubEntry() => new()
    {
        { "glob", "/workspace/repo" },
        { "directory_read", "/workspace/repo" },
    };

    private IBaseTool NewTool(string name) => name switch
    {
        "glob" => new GlobTool(_service, new StubPathValidator().AllowAll()),
        "grep" => new GrepTool(_service, new StubPathValidator().AllowAll()),
        _ => new DirectoryReadTool(_service, new StubPathValidator().AllowAll()),
    };

    private static Dictionary<string, object?> Parameters(string name, string path) => name switch
    {
        "glob" => new() { ["pattern"] = "**/*.txt", [ParamPath] = path },
        "grep" => new() { ["pattern"] = "needle", [ParamPath] = path },
        _ => new() { [ParamPath] = path, ["recursive"] = true },
    };

    [Theory]
    [MemberData(nameof(Searches))]
    [Trait("BR", "#349")]
    public Task ShouldNeverReturnAFileOutsideTheScope_WhenAScopedAgentSearches(string tool, string path)
        => AssertNoFileOutsideTheScopeAsync(tool, path);

    [Theory]
    [MemberData(nameof(ListingsOverAWithheldSubEntry))]
    [Trait("BR", "#349")]
    public Task ShouldNotListAWithheldSubEntry_WhenAScopedAgentListsInsideItsScope(string tool, string path)
        => AssertNoFileOutsideTheScopeAsync(tool, path);

    private async Task AssertNoFileOutsideTheScopeAsync(string tool, string path)
    {
        var search = NewTool(tool);
        try
        {
            ToolCallResponse response;
            await using (AgentRunScope.Begin("id", "reader", null, Scope()))
                response = await search.CallAsync(new ToolCallRequest(tool, Parameters(tool, path)), TestContext.Current.CancellationToken);

            // Either the call is refused, or what it answers names no file the scope withholds.
            var answered = JsonSerializer.Serialize(response.Result);
            Assert.DoesNotContain("secret", answered, StringComparison.Ordinal);
            Assert.DoesNotContain(_tempDir, response.Error ?? "", StringComparison.Ordinal);
            if (response.Success && path == "/workspace/repo")
                Assert.Contains("open.txt", answered, StringComparison.Ordinal);
        }
        finally
        {
            (search as IDisposable)?.Dispose();
        }
    }
}
