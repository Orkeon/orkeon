using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Queries;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.Tests.Doubles;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class FileReadToolCacheTests : IDisposable
{
    private readonly string _testDir;

    public FileReadToolCacheTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"fileread_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UseRaggableCache_hit_returns_indexed_snippet_when_hash_matches()
    {
        var path = Path.Combine(_testDir, "a.ts");
        var disk = "export const X = 1;\n";
        await File.WriteAllTextAsync(path, disk, TestContext.Current.CancellationToken);

        var cachedSnippet = "// FROM INDEX\nexport const X = 1;\n";
        var store = new StubRaggableStore(
            modulesByPath: new() { [path] = (Sha256: Sha256OfUtf8(disk), Snippet: cachedSnippet) });

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal(cachedSnippet, dict["content"]?.ToString());
        Assert.Equal(true, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_miss_falls_back_to_disk_when_hash_mismatch()
    {
        var path = Path.Combine(_testDir, "b.ts");
        var disk = "export const X = 2;\n";
        await File.WriteAllTextAsync(path, disk, TestContext.Current.CancellationToken);

        var store = new StubRaggableStore(
            modulesByPath: new() { [path] = (Sha256: "deadbeef", Snippet: "stale cached content") });

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal(disk, dict["content"]?.ToString());
        Assert.Equal(false, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_without_store_reads_from_disk()
    {
        var path = Path.Combine(_testDir, "c.ts");
        await File.WriteAllTextAsync(path, "plain", TestContext.Current.CancellationToken);

        using var tool = new FileReadTool(FakeFs(), FakePv(), raggableStore: null);
        var dict = await CallAndGetDictAsync(tool, path, useCache: true);

        Assert.Equal("plain", dict["content"]?.ToString());
        Assert.Equal(false, dict["served_from_cache"]);
    }

    [Fact]
    public async Task UseRaggableCache_false_never_queries_the_store()
    {
        var path = Path.Combine(_testDir, "d.ts");
        await File.WriteAllTextAsync(path, "raw", TestContext.Current.CancellationToken);
        var store = new StubRaggableStore(modulesByPath: []);

        using var tool = new FileReadTool(FakeFs(), FakePv(), store);
        var dict = await CallAndGetDictAsync(tool, path, useCache: false);

        Assert.Equal("raw", dict["content"]?.ToString());
        Assert.Equal(0, store.GetModuleByPathCalls);
    }

    private static async Task<Dictionary<string, object?>> CallAndGetDictAsync(FileReadTool tool, string path, bool useCache)
    {
        var resp = await tool.CallAsync(new ToolCallRequest(
            "file_read",
            new Dictionary<string, object?> { ["path"] = path, ["use_raggable_cache"] = useCache }));
        Assert.True(resp.Success, resp.Error ?? "expected success");
        var dict = resp.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict!;
    }

    private static string Sha256OfUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static PassThroughFileSystemService FakeFs() => new();

    private static StubPathValidator FakePv() => new StubPathValidator().AllowAll();
}
