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

public sealed class FileReadToolRangeTests : IDisposable
{
    private readonly string _testDir;

    public FileReadToolRangeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"fileread_range_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Lines(int count, string prefix = "l") =>
        string.Join("\n", Enumerable.Range(1, count).Select(i => prefix + i));

    private static FileReadTool NewTool(IRaggableStore? store = null) =>
        new(new PassThroughFileSystemService(), new StubPathValidator().AllowAll(), store);

    private static Task<ToolCallResponse> CallAsync(FileReadTool tool, Dictionary<string, object?> p) =>
        tool.CallAsync(new ToolCallRequest("file_read", p));

    private static Dictionary<string, object?> Ok(ToolCallResponse r)
    {
        Assert.True(r.Success, r.Error ?? "expected success");
        var d = r.Result as Dictionary<string, object?>;
        Assert.NotNull(d);
        return d!;
    }

    private static string Body(Dictionary<string, object?> d) =>
        (d["content"]?.ToString() ?? "").ReplaceLineEndings("\n").TrimEnd('\n');

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReturnRequestedLines_WhenOffsetAndLimitGiven()
    {
        var path = CreateFile("a.txt", Lines(10));
        using var tool = NewTool();

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 3, ["limit"] = 2 }));

        Assert.Equal("l3\nl4", Body(d));
        Assert.Equal(3, Convert.ToInt32(d["start_line"]));
        Assert.Equal(4, Convert.ToInt32(d["end_line"]));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReportContinuation_WhenRangeEndsBeforeEof()
    {
        var path = CreateFile("a.txt", Lines(10));
        using var tool = NewTool();

        var more = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 2, ["limit"] = 3 }));
        var end = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 8, ["limit"] = 3 }));

        Assert.Equal(true, more["has_more"]);
        Assert.Equal(4, Convert.ToInt32(more["end_line"]));
        Assert.Equal(false, end["has_more"]);
        Assert.Equal(10, Convert.ToInt32(end["end_line"]));
        Assert.Equal("l8\nl9\nl10", Body(end));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuseRange_WhenOffsetBeyondFile()
    {
        var path = CreateFile("a.txt", Lines(3));
        using var tool = NewTool();

        var r = await CallAsync(tool, new() { ["path"] = path, ["offset"] = 10, ["limit"] = 2 });

        Assert.False(r.Success);
        Assert.Contains("offset", r.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldNotReadWholeFile_WhenRangeGiven()
    {
        var path = CreateFile("big.txt", Lines(200_000, "line number "));
        var fs = new FakeCountingFileSystemService(new PassThroughFileSystemService());
        using var tool = new FileReadTool(fs, new StubPathValidator().AllowAll());

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 10, ["limit"] = 5 }));

        Assert.Equal("line number 10\nline number 11\nline number 12\nline number 13\nline number 14", Body(d));
        Assert.Equal(0, fs.WholeFileReads);
        Assert.InRange(fs.BytesRead, 0, 16384);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldCutRangeFromCachedContent_WhenUseRaggableCacheWithRange()
    {
        var disk = Lines(10);
        var path = CreateFile("a.ts", disk);
        var store = new StubRaggableStore(new() { [path] = (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(disk))), disk) });
        using var tool = NewTool(store);
        var p = new Dictionary<string, object?> { ["path"] = path, ["offset"] = 4, ["limit"] = 3 };

        var plain = Ok(await CallAsync(tool, new(p) { ["use_raggable_cache"] = false }));
        var cached = Ok(await CallAsync(tool, new(p) { ["use_raggable_cache"] = true }));

        Assert.Equal(true, cached["served_from_cache"]);
        Assert.Equal("l4\nl5\nl6", Body(cached));
        Assert.Equal(Body(plain), Body(cached));
        Assert.Equal(6, Convert.ToInt32(cached["end_line"]));
        Assert.Equal(plain["end_line"], cached["end_line"]);
        Assert.Equal(true, cached["has_more"]);
    }

    [Theory]
    [Trait("BR", "RT-09a")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ShouldRefuse_WhenOffsetIsZeroOrNegative(int offset)
    {
        var path = CreateFile("a.txt", Lines(3));
        using var tool = NewTool();

        var r = await CallAsync(tool, new() { ["path"] = path, ["offset"] = offset, ["limit"] = 2 });

        Assert.False(r.Success);
        Assert.Contains("offset", r.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("BR", "RT-09a")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ShouldRefuse_WhenLimitIsZeroOrNegative(int limit)
    {
        var path = CreateFile("a.txt", Lines(3));
        using var tool = NewTool();

        var r = await CallAsync(tool, new() { ["path"] = path, ["offset"] = 1, ["limit"] = limit });

        Assert.False(r.Success);
        Assert.Contains("limit", r.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReadToEof_WhenOffsetGivenWithoutLimit()
    {
        var path = CreateFile("a.txt", Lines(6));
        using var tool = NewTool();

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 4 }));

        Assert.Equal("l4\nl5\nl6", Body(d));
        Assert.Equal(6, Convert.ToInt32(d["end_line"]));
        Assert.Equal(false, d["has_more"]);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReturnEmptyContentWithoutMore_WhenEmptyFileAtOffsetOne()
    {
        var path = CreateFile("empty.txt", "");
        using var tool = NewTool();

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 1, ["limit"] = 5 }));

        Assert.Equal("", d["content"]?.ToString());
        Assert.Equal(1, Convert.ToInt32(d["start_line"]));
        Assert.Equal(false, d["has_more"]);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldCapLimit_WhenLimitExceedsExistingLineCap()
    {
        // The existing cap is max_length (FileReadTool.cs:140-145): a larger limit is capped by it, not refused.
        var path = CreateFile("a.txt", Lines(100));
        using var tool = NewTool();

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 1, ["limit"] = 1000, ["max_length"] = 12 }));

        Assert.Equal(true, d["truncated"]);
        Assert.True(d["content"]!.ToString()!.Length <= 12);
        Assert.Equal(true, d["has_more"]);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldApplyCharacterCapAfterRange_WhenMaxLengthCutsRange()
    {
        var path = CreateFile("a.txt", Lines(10, "line"));
        using var tool = NewTool();

        var d = Ok(await CallAsync(tool, new() { ["path"] = path, ["offset"] = 2, ["limit"] = 6, ["max_length"] = 12 }));

        // "line2\nline3\nli" would be 14 chars: the cut keeps whole lines only.
        Assert.Equal(true, d["truncated"]);
        Assert.Equal("line2\nline3", Body(d));
        Assert.Equal(3, Convert.ToInt32(d["end_line"]));
        Assert.Equal(true, d["has_more"]);
    }
}
