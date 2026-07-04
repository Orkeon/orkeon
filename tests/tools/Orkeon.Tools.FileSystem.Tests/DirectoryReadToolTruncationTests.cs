using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

/// <summary>
/// Covers the truncation / max-results budgeting and glob-pattern normalization branches
/// of <see cref="DirectoryReadTool"/> not exercised by <see cref="DirectoryReadToolTests"/>.
/// </summary>
public sealed class DirectoryReadToolTruncationTests : IDisposable
{
    private readonly DirectoryReadTool _tool;
    private readonly string _testDir;

    public DirectoryReadToolTruncationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dirread_trunc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _tool = new DirectoryReadTool(new PassThroughFileSystemService(), new StubPathValidator().AllowAll());
    }

    private async Task<Dictionary<string, object?>> CallAsync(Dictionary<string, object?> parameters)
    {
        var response = await _tool.CallAsync(new ToolCallRequest("directory_read", parameters));
        Assert.True(response.Success, response.Error ?? "expected success");
        var dict = response.Result as Dictionary<string, object?>;
        Assert.NotNull(dict);
        return dict!;
    }

    private void CreateFiles(int count)
    {
        for (int i = 0; i < count; i++)
            File.WriteAllText(Path.Combine(_testDir, $"f{i:D4}.txt"), "x");
    }

    // -- Truncation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldTruncate_WhenEntriesExceedMaxResults()
    {
        CreateFiles(10);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["max_results"] = 3
        });

        Assert.True((bool)dict["truncated"]!);
        Assert.Equal(10, (int)dict["total_files"]!);
        Assert.Equal(10, (int)dict["total_entries_before_truncation"]!);

        var files = dict["files"] as IEnumerable<object?>;
        Assert.NotNull(files);
        Assert.Equal(3, files!.Count());
    }

    [Fact]
    public async Task ShouldNotTruncate_WhenUnderMaxResults()
    {
        CreateFiles(2);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["max_results"] = 100
        });

        Assert.False((bool)dict["truncated"]!);
        // total_entries_before_truncation is null when not truncated => key absent
        Assert.False(dict.TryGetValue("total_entries_before_truncation", out var t) && t is not null);
    }

    [Fact]
    public async Task ShouldUseDefaultMaxResults_WhenZeroOrNegativeRequested()
    {
        CreateFiles(5);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["max_results"] = 0 // falls back to DefaultMaxResults (500)
        });

        Assert.False((bool)dict["truncated"]!);
        Assert.Equal(5, (int)dict["total_files"]!);
    }

    // -- Pattern normalization ───────────────────────────────────────────

    [Fact]
    public async Task ShouldStripGlobStarPrefix_AndStillMatch()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "code.cs"), "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "notes.txt"), "x", TestContext.Current.CancellationToken);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["pattern"] = "**/*.cs",
            ["recursive"] = true
        });

        Assert.Equal(1, (int)dict["total_files"]!);
    }

    [Fact]
    public async Task ShouldMatchEverything_WhenPatternIsDoubleStarOnly()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.cs"), "x", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "b.txt"), "x", TestContext.Current.CancellationToken);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["pattern"] = "**"
        });

        Assert.Equal(2, (int)dict["total_files"]!);
    }

    [Fact]
    public async Task ShouldDefaultPattern_WhenPatternIsWhitespace()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.cs"), "x", TestContext.Current.CancellationToken);

        var dict = await CallAsync(new Dictionary<string, object?>
        {
            [ParamPath] = _testDir,
            ["pattern"] = "   "
        });

        Assert.Equal(1, (int)dict["total_files"]!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { /* best effort */ }
        _tool.Dispose();
    }
}
