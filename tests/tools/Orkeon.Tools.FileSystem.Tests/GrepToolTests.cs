using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class GrepToolTests : IDisposable
{
    private const FileAccessRights Rd = FileAccessRights.Read;

    private readonly FakeRightsFileSystemService _fs;
    private readonly GrepTool _tool;

    public GrepToolTests()
    {
        _fs = new FakeRightsFileSystemService().AddMount("/workspace", Rd);
        _tool = new GrepTool(_fs, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private void Put(string path, string text) => _fs.AddFile(path, Encoding.UTF8.GetBytes(text));

    private static Task<ToolCallResponse> GrepAsync(
        GrepTool tool, string pattern, string path, string? glob = null, bool? ignoreCase = null, int? max = null, int? maxFiles = null,
        string? mode = null)
    {
        var p = new Dictionary<string, object?> { ["pattern"] = pattern, [ParamPath] = path };
        if (glob is not null) p["glob"] = glob;
        if (ignoreCase is { } c) p["case_insensitive"] = c;
        if (max is { } m) p["max_results"] = m;
        if (maxFiles is { } f) p["max_files"] = f;
        if (mode is not null) p["output_mode"] = mode;
        return tool.CallAsync(new ToolCallRequest("grep", p));
    }

    private Task<ToolCallResponse> GrepAsync(
        string pattern, string path, string? glob = null, bool? ignoreCase = null, int? max = null, int? maxFiles = null, string? mode = null)
        => GrepAsync(_tool, pattern, path, glob, ignoreCase, max, maxFiles, mode);

    private sealed record Hit(string Path, int Line, string Text);

    private sealed record FileCount(string Path, int Count);

    private sealed record GrepView(
        IReadOnlyList<Hit> Hits, bool Truncated, bool TimedOut, int Scanned, int Skipped,
        IReadOnlyList<FileCount> Counts, int TotalMatches, int FilesMatched, bool ScanComplete);

    private static GrepView View(ToolCallResponse r)
    {
        var d = Assert.IsType<Dictionary<string, object?>>(r.Result);
        var hits = ((System.Collections.IEnumerable)d["hits"]!).Cast<object>()
            .Select(o => Assert.IsType<Dictionary<string, object?>>(o))
            .Select(h => new Hit((string)h["path"]!, Convert.ToInt32(h["line"]), (string)h["text"]!))
            .ToList();
        var counts = ((System.Collections.IEnumerable)d["counts"]!).Cast<object>()
            .Select(o => Assert.IsType<Dictionary<string, object?>>(o))
            .Select(c => new FileCount((string)c["path"]!, Convert.ToInt32(c["count"])))
            .ToList();
        return new GrepView(
            hits,
            Convert.ToBoolean(d["truncated"]),
            Convert.ToBoolean(d["timed_out"]),
            Convert.ToInt32(d["files_scanned"]),
            Convert.ToInt32(d["files_skipped"]),
            counts,
            Convert.ToInt32(d["total_matches"]),
            Convert.ToInt32(d["files_matched"]),
            Convert.ToBoolean(d["scan_complete"]));
    }

    private static GrepView Ok(ToolCallResponse r)
    {
        Assert.True(r.Success, r.Error ?? "Expected success");
        return View(r);
    }

    private static bool ReturnsHits(ToolCallResponse r) => r.Result is Dictionary<string, object?> && View(r).Hits.Count > 0;

    private static void AssertRefused(ToolCallResponse r, string because)
    {
        Assert.False(r.Success, because);
        Assert.False(string.IsNullOrWhiteSpace(r.Error), because);
        Assert.False(ReturnsHits(r), because);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnPathLineText_WhenPatternMatches()
    {
        Put("/workspace/proj/a.txt", "alpha\nbeta needle here\ngamma\n");
        Put("/workspace/proj/sub/b.txt", "none\nnone\nneedle\n");

        var res = Ok(await GrepAsync("needle", "/workspace/proj"));

        Assert.Equal(
            [new Hit("/workspace/proj/a.txt", 2, "beta needle here"), new Hit("/workspace/proj/sub/b.txt", 3, "needle")],
            res.Hits.OrderBy(h => h.Path, StringComparer.Ordinal).ToList());
        Assert.False(res.Truncated);
        Assert.False(res.TimedOut);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldCapHits_WhenMatchesExceedMaxResults()
    {
        Put("/workspace/proj/a.txt", "needle 1\nneedle 2\nneedle 3\n");

        var capped = Ok(await GrepAsync("needle", "/workspace/proj", max: 2));
        var exact = Ok(await GrepAsync("needle", "/workspace/proj", max: 3));
        var roomy = Ok(await GrepAsync("needle", "/workspace/proj", max: 10));

        Assert.Equal(2, capped.Hits.Count);
        Assert.True(capped.Truncated);
        Assert.Equal(3, exact.Hits.Count);
        Assert.False(exact.Truncated);
        Assert.Equal(3, roomy.Hits.Count);
        Assert.False(roomy.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldSkipBinaryFiles_WhenCandidateContainsNul()
    {
        Put("/workspace/proj/ok.txt", "needle\n");
        _fs.AddFile("/workspace/proj/nul.bin", [.. "needle"u8, 0x00, .. "needle"u8]);
        _fs.AddFile("/workspace/proj/latin.dat", [.. "needle "u8, 0xC3, 0x28]);

        var res = Ok(await GrepAsync("needle", "/workspace/proj"));

        Assert.Equal(["/workspace/proj/ok.txt"], res.Hits.Select(h => h.Path));
        Assert.Equal(1, res.Scanned);
        Assert.Equal(2, res.Skipped);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefusePattern_WhenRegexIsInvalid()
    {
        Put("/workspace/proj/a.txt", "needle (x\n");
        Assert.Single(Ok(await GrepAsync("needle", "/workspace/proj")).Hits);

        var r = await GrepAsync("(unclosed", "/workspace/proj");

        AssertRefused(r, "invalid regex");
        Assert.Contains("(unclosed", r.Error);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGrep_WhenRootNotMountedOrNotDirectory()
    {
        Put("/workspace/proj/a.txt", "needle\n");
        Assert.Single(Ok(await GrepAsync("needle", "/workspace/proj")).Hits);

        foreach (var path in new[] { "/elsewhere/proj", "/workspace/missing", "/workspace/proj/a.txt" })
            AssertRefused(await GrepAsync("needle", path), path);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGrep_WhenPathOutsideAgentRoot()
    {
        Put("/workspace/job/x.txt", "needle\n");
        Put("/workspace/other/y.txt", "needle\n");
        await using var scope = AgentRunScope.Begin("agent-1", "reader", "/workspace/job");

        var inside = Ok(await GrepAsync("needle", "/workspace/job"));
        var outside = await GrepAsync("needle", "/workspace/other");

        Assert.Equal(["/workspace/job/x.txt"], inside.Hits.Select(h => h.Path));
        AssertRefused(outside, "outside agent root");
        Assert.DoesNotContain(Path.GetTempPath(), outside.Error);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldFilterCandidates_WhenGlobGiven()
    {
        Put("/workspace/proj/a.cs", "needle\n");
        Put("/workspace/proj/deep/b.cs", "needle\n");
        Put("/workspace/proj/c.txt", "needle\n");

        var filtered = Ok(await GrepAsync("needle", "/workspace/proj", glob: "**/*.cs"));
        var all = Ok(await GrepAsync("needle", "/workspace/proj"));
        var absolute = await GrepAsync("needle", "/workspace/proj", glob: "/workspace/proj/**/*.cs");

        Assert.Equal(
            ["/workspace/proj/a.cs", "/workspace/proj/deep/b.cs"],
            filtered.Hits.Select(h => h.Path).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(3, all.Hits.Count);
        AssertRefused(absolute, "absolute glob");
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldMatchCaseInsensitively_WhenFlagSet()
    {
        Put("/workspace/proj/a.txt", "NEEDLE upper\n");

        var sensitive = Ok(await GrepAsync("needle", "/workspace/proj", ignoreCase: false));
        var insensitive = Ok(await GrepAsync("needle", "/workspace/proj", ignoreCase: true));

        Assert.Empty(sensitive.Hits);
        Assert.Equal([new Hit("/workspace/proj/a.txt", 1, "NEEDLE upper")], insensitive.Hits);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldFlagTimedOut_WhenRegexExceedsTimeout()
    {
        Put("/workspace/proj/a.txt", "hit\n" + new string('a', 60) + "!\n");
        using var tool = new GrepTool(_fs, new StubPathValidator().AllowAll(), matchTimeout: TimeSpan.FromMilliseconds(20));

        var res = Ok(await GrepAsync(tool, "^hit$|(a+)+$", "/workspace/proj"));

        Assert.True(res.TimedOut);
        Assert.Equal([new Hit("/workspace/proj/a.txt", 1, "hit")], res.Hits);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldDecodeBomAndUtf16_WhenFileHasBom()
    {
        _fs.AddFile("/workspace/proj/u8.txt", [.. Encoding.UTF8.GetPreamble(), .. "needle utf8"u8]);
        _fs.AddFile("/workspace/proj/u16.txt", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("first\nneedle utf16\n")]);

        var res = Ok(await GrepAsync("needle", "/workspace/proj"));

        Assert.Equal(
            [new Hit("/workspace/proj/u16.txt", 2, "needle utf16"), new Hit("/workspace/proj/u8.txt", 1, "needle utf8")],
            res.Hits.OrderBy(h => h.Path, StringComparer.Ordinal).ToList());
        Assert.Equal(2, res.Scanned);
        Assert.Equal(0, res.Skipped);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldTrimTrailingCr_WhenFileIsCrlf()
    {
        Put("/workspace/proj/a.txt", "first\r\nneedle line\r\nlast\r\n");

        var res = Ok(await GrepAsync("needle", "/workspace/proj"));

        Assert.Equal([new Hit("/workspace/proj/a.txt", 2, "needle line")], res.Hits);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGrep_WhenMaxFilesOrMaxResultsNotPositive()
    {
        Put("/workspace/proj/a.txt", "needle\n");
        Assert.Single(Ok(await GrepAsync("needle", "/workspace/proj", max: 1, maxFiles: 1)).Hits);

        AssertRefused(await GrepAsync("needle", "/workspace/proj", maxFiles: 0), "max_files 0");
        AssertRefused(await GrepAsync("needle", "/workspace/proj", maxFiles: -1), "max_files -1");
        AssertRefused(await GrepAsync("needle", "/workspace/proj", max: 0), "max_results 0");
        AssertRefused(await GrepAsync("needle", "/workspace/proj", max: -1), "max_results -1");
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldCutLongHitLine_WhenLineExceeds500Chars()
    {
        Put("/workspace/proj/a.txt", new string('x', 600) + "\nxxxxx short\n");

        var res = Ok(await GrepAsync("xxxxx", "/workspace/proj"));

        var cut = res.Hits.Single(h => h.Line == 1).Text;
        Assert.StartsWith(new string('x', 500), cut);
        Assert.EndsWith(GrepTool.LineCutMarker, cut);
        Assert.Equal(500 + GrepTool.LineCutMarker.Length, cut.Length);
        Assert.Equal("xxxxx short", res.Hits.Single(h => h.Line == 2).Text);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldSkipAndCountUnreadableFile_WhenReadFails()
    {
        Put("/workspace/proj/a.txt", "needle\n");
        Put("/workspace/proj/b.txt", "needle\n");
        var failing = new FakeFailingReadFileSystemService(_fs, "/workspace/proj/a.txt");
        using var tool = new GrepTool(failing, new StubPathValidator().AllowAll());

        var res = Ok(await GrepAsync(tool, "needle", "/workspace/proj"));

        Assert.Equal(["/workspace/proj/b.txt"], res.Hits.Select(h => h.Path));
        Assert.Equal(1, res.Skipped);
        Assert.Equal(1, res.Scanned);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldFlagTruncated_WhenMaxFilesIsHit()
    {
        for (var i = 0; i < 5; i++) Put($"/workspace/proj/f{i}.txt", "needle\n");

        var limited = Ok(await GrepAsync("needle", "/workspace/proj", maxFiles: 3));
        var roomy = Ok(await GrepAsync("needle", "/workspace/proj", maxFiles: 5));

        Assert.Equal(3, limited.Scanned);
        Assert.True(limited.Truncated);
        Assert.Equal(5, roomy.Scanned);
        Assert.False(roomy.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReadEachCandidateFileOnce_WhenScanning()
    {
        Put("/workspace/proj/a.txt", "needle\n");
        Put("/workspace/proj/b.txt", "needle\n");
        _fs.AddFile("/workspace/proj/c.bin", [.. "needle"u8, 0x00]);
        var counting = new FakeCountingFileSystemService(_fs);
        using var tool = new GrepTool(counting, new StubPathValidator().AllowAll());

        var res = Ok(await GrepAsync(tool, "needle", "/workspace/proj"));

        Assert.Equal(2, res.Scanned);
        Assert.Equal(
            ["/workspace/proj/a.txt", "/workspace/proj/b.txt", "/workspace/proj/c.bin"],
            counting.Reads.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public void ShouldBeReadAccess_WhenAccessAndSchedulerQueried()
    {
        Assert.Equal(ToolAccess.Read, _tool.Access);
        Assert.True(ToolCallScheduler.IsParallelRead("grep", ToolAccess.Read));
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnWholeHitsOnly_WhenResponseExceedsTotalCharacterBound()
    {
        var line = new string('x', 400);
        Put("/workspace/big/a.txt", string.Join('\n', Enumerable.Repeat(line, 300)));

        var r = await GrepAsync("xxxxx", "/workspace/big", max: 1000);
        var res = Ok(r);

        Assert.True(res.Truncated);
        Assert.True(r.AlreadyBounded);
        Assert.NotEmpty(res.Hits);
        Assert.True(res.Hits.Count < 300);
        Assert.Equal(Enumerable.Range(1, res.Hits.Count), res.Hits.Select(h => h.Line));
        Assert.All(res.Hits, h => Assert.Equal(line, h.Text));
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public void ShouldResolveGrepTool_WhenFileSystemToolsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(_fs);
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystemTools();
        using var provider = services.BuildServiceProvider();

        var tool = provider.GetServices<IBaseTool>().SingleOrDefault(t => t.Name == "grep");

        Assert.IsType<GrepTool>(tool);
    }

    [Theory]
    [Trait("BR", "RT-09b")]
    [InlineData("../other/secret.txt", true)]
    [InlineData("src/../../other/secret.txt", true)]
    [InlineData("..\\other\\secret.txt", true)]
    [InlineData("**/*.txt", false)]
    [InlineData("x.txt", false)]
    [InlineData("a..b.txt", false)]
    public async Task ShouldRefuseGrep_WhenGlobHasParentSegment(string glob, bool refused)
    {
        Put("/workspace/job/x.txt", "needle\n");
        Put("/workspace/job/a..b.txt", "needle\n");
        Put("/workspace/other/secret.txt", "needle secret\n");
        await using var scope = AgentRunScope.Begin("agent-1", "reader", "/workspace/job");

        var r = await GrepAsync("needle", "/workspace/job", glob: glob);

        if (refused)
        {
            AssertRefused(r, glob);
        }
        else
        {
            var res = Ok(r);
            Assert.NotEmpty(res.Hits);
            Assert.DoesNotContain(res.Hits, h => h.Path.StartsWith("/workspace/other", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldSkipOversizedFile_WhenFileExceedsSizeLimit()
    {
        const int limit = 1_048_576;
        string Body(int size) => "needle\n" + new string('x', size - 7);
        Put("/workspace/proj/big.txt", Body(limit + 1));
        Put("/workspace/proj/exact.txt", Body(limit));
        Put("/workspace/proj/small.txt", "needle\n");
        var counting = new FakeCountingFileSystemService(_fs);
        using var tool = new GrepTool(counting, new StubPathValidator().AllowAll());

        var res = Ok(await GrepAsync(tool, "needle", "/workspace/proj"));

        Assert.Equal(
            ["/workspace/proj/exact.txt", "/workspace/proj/small.txt"],
            res.Hits.Select(h => h.Path).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(1, res.Skipped);
        Assert.Equal(2, res.Scanned);
        Assert.DoesNotContain("/workspace/proj/big.txt", counting.Reads);
    }

    [Theory]
    [Trait("BR", "RT-09b")]
    [InlineData("big.txt", "/workspace/proj/big.txt", true)]
    [InlineData("./big.txt", "/workspace/proj/big.txt", true)]
    [InlineData("sub//big.txt", "/workspace/proj/sub/big.txt", true)]
    [InlineData("./small.txt", "/workspace/proj/small.txt", false)]
    public async Task ShouldSkipOversizedFile_WhenGlobNamesFileInNonNormalisedForm(string glob, string target, bool oversized)
    {
        var big = "needle\n" + new string('x', 1_048_576 + 1 - 7);
        Put("/workspace/proj/big.txt", big);
        Put("/workspace/proj/sub/big.txt", big);
        Put("/workspace/proj/small.txt", "needle\n");
        var counting = new FakeCountingFileSystemService(_fs);
        using var tool = new GrepTool(new FakeNormalisingFileSystemService(counting), new StubPathValidator().AllowAll());

        var res = Ok(await GrepAsync(tool, "needle", "/workspace/proj", glob: glob));

        if (oversized)
        {
            Assert.Empty(res.Hits);
            Assert.Equal(1, res.Skipped);
            Assert.Equal(0, res.Scanned);
            Assert.DoesNotContain(target, counting.Reads);
        }
        else
        {
            var hit = Assert.Single(res.Hits);
            Assert.Equal((1, "needle"), (hit.Line, hit.Text));
            Assert.Equal(1, res.Scanned);
            Assert.Equal(0, res.Skipped);
        }
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public async Task ShouldGrepRelativePath_AndEchoAbsolutePaths()
    {
        var root = Directory.CreateTempSubdirectory("orkeon-relative-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            await File.WriteAllTextAsync(Path.Combine(root, "src", "stats.js"), "sum\nsum\n", TestContext.Current.CancellationToken);
            var fs = new DiskBackedFileSystemService(root, "/workspace") { WorkingDirectory = "/workspace" };
            using var tool = new GrepTool(fs, new StubPathValidator().AllowAll());

            var result = await tool.CallAsync(new ToolCallRequest(
                ToolName: "grep",
                Parameters: new Dictionary<string, object?> { ["pattern"] = "sum", ["path"] = "src" }), TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.Error);
            var dict = Assert.IsType<Dictionary<string, object?>>(result.Result, exactMatch: false);
            var hits = ((System.Collections.IEnumerable)dict["hits"]!).Cast<Dictionary<string, object?>>().ToList();
            Assert.Equal(2, hits.Count);
            Assert.All(hits, h => Assert.Equal("/workspace/src/stats.js", h["path"]));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnCountsWithoutHits_WhenOutputModeIsCount()
    {
        Put("/workspace/proj/a.cs", "sealed class A\nsealed class B\nclass C\n");
        Put("/workspace/proj/b.cs", "sealed class D\n");
        Put("/workspace/proj/c.cs", "class E\n");
        Put("/workspace/proj/d.cs", "sealed class F\nsealed class G\nsealed class H\n");

        var res = Ok(await GrepAsync("^sealed class", "/workspace/proj", glob: "**/*.cs", mode: "count"));

        Assert.Empty(res.Hits);
        Assert.Equal(6, res.TotalMatches);
        Assert.Equal(3, res.FilesMatched);
        Assert.Equal(4, res.Scanned);
        Assert.Equal(
            [new FileCount("/workspace/proj/d.cs", 3), new FileCount("/workspace/proj/a.cs", 2), new FileCount("/workspace/proj/b.cs", 1)],
            res.Counts);
        Assert.True(res.ScanComplete);
        Assert.False(res.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldCapPerFileCountsButNotTotals_WhenCountModeExceedsMaxResults()
    {
        for (var i = 0; i < 5; i++) Put($"/workspace/proj/f{i}.txt", string.Concat(Enumerable.Repeat("needle\n", i + 1)));

        var res = Ok(await GrepAsync("needle", "/workspace/proj", max: 2, mode: "count"));

        Assert.Equal(15, res.TotalMatches);
        Assert.Equal(5, res.FilesMatched);
        Assert.Equal([new FileCount("/workspace/proj/f4.txt", 5), new FileCount("/workspace/proj/f3.txt", 4)], res.Counts);
        Assert.True(res.ScanComplete);
        Assert.False(res.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldFlagIncompleteScan_WhenCountModeHitsMaxFiles()
    {
        for (var i = 0; i < 5; i++) Put($"/workspace/proj/f{i}.txt", "needle\n");

        var limited = Ok(await GrepAsync("needle", "/workspace/proj", maxFiles: 3, mode: "count"));
        var roomy = Ok(await GrepAsync("needle", "/workspace/proj", maxFiles: 5, mode: "count"));

        Assert.Equal(3, limited.TotalMatches);
        Assert.True(limited.Truncated);
        Assert.False(limited.ScanComplete);
        Assert.Equal(5, roomy.TotalMatches);
        Assert.False(roomy.Truncated);
        Assert.True(roomy.ScanComplete);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReportTotalMatches_WhenHitsAreCutAtMaxResultsButScanIsComplete()
    {
        Put("/workspace/proj/a.txt", "needle 1\nneedle 2\nneedle 3\n");
        Put("/workspace/proj/b.txt", "needle 4\nnone\n");

        var res = Ok(await GrepAsync("needle", "/workspace/proj", max: 2));

        Assert.Equal(2, res.Hits.Count);
        Assert.True(res.Truncated);
        Assert.True(res.ScanComplete);
        Assert.Equal(4, res.TotalMatches);
        Assert.Equal(2, res.FilesMatched);
        Assert.Equal(2, res.Scanned);
        Assert.Empty(res.Counts);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGrep_WhenOutputModeIsUnknown()
    {
        Put("/workspace/proj/a.txt", "needle\n");

        var res = await GrepAsync("needle", "/workspace/proj", mode: "lines");

        AssertRefused(res, "an unknown output_mode is refused");
        Assert.Contains("output_mode", res.Error, StringComparison.Ordinal);
    }
}
