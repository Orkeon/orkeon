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
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class GlobToolTests : IDisposable
{
    private const FileAccessRights Rd = FileAccessRights.Read;

    private readonly FakeRightsFileSystemService _fs;
    private readonly GlobTool _tool;

    public GlobToolTests()
    {
        _fs = new FakeRightsFileSystemService().AddMount("/workspace", Rd);
        Add("/workspace/proj/lib/a.cs");
        Add("/workspace/proj/src/b.cs");
        Add("/workspace/proj/src/deep/c.cs");
        Add("/workspace/proj/src/d.txt");
        Add("/workspace/proj/readme.md");
        Add("/workspace/job/x.cs");
        Add("/workspace/other/y.cs");
        _tool = new GlobTool(_fs, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private void Add(string path) => _fs.AddFile(path, Encoding.UTF8.GetBytes("x"));

    private Task<ToolCallResponse> GlobAsync(string pattern, string path, int? max = null)
    {
        var p = new Dictionary<string, object?> { ["pattern"] = pattern, [ParamPath] = path };
        if (max is { } m) p["max_results"] = m;
        return _tool.CallAsync(new ToolCallRequest("glob", p));
    }

    private sealed record GlobView(IReadOnlyList<string> Paths, bool Truncated);

    private static GlobView View(ToolCallResponse r)
    {
        var d = Assert.IsType<Dictionary<string, object?>>(r.Result);
        return new GlobView(
            ((System.Collections.IEnumerable)d["paths"]!).Cast<object>().Select(o => (string)o).ToList(),
            Convert.ToBoolean(d["truncated"]));
    }

    private static GlobView Ok(ToolCallResponse r)
    {
        Assert.True(r.Success, r.Error ?? "Expected success");
        return View(r);
    }

    private static bool ReturnsPaths(ToolCallResponse r) => r.Result is Dictionary<string, object?> && View(r).Paths.Count > 0;

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnMatchingVirtualPaths_WhenGlobPatternGiven()
    {
        var res = Ok(await GlobAsync("**/*.cs", "/workspace/proj"));

        Assert.Equal(
            ["/workspace/proj/lib/a.cs", "/workspace/proj/src/b.cs", "/workspace/proj/src/deep/c.cs"],
            res.Paths);
        Assert.False(res.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldCapAndFlagTruncated_WhenMatchesExceedMaxResults()
    {
        var capped = Ok(await GlobAsync("**/*.cs", "/workspace/proj", 2));
        var exact = Ok(await GlobAsync("**/*.cs", "/workspace/proj", 3));
        var roomy = Ok(await GlobAsync("**/*.cs", "/workspace/proj", 10));
        var zero = await GlobAsync("**/*.cs", "/workspace/proj", 0);

        Assert.Equal(["/workspace/proj/lib/a.cs", "/workspace/proj/src/b.cs"], capped.Paths);
        Assert.True(capped.Truncated);
        Assert.Equal(3, exact.Paths.Count);
        Assert.False(exact.Truncated);
        Assert.Equal(3, roomy.Paths.Count);
        Assert.False(roomy.Truncated);
        Assert.False(zero.Success);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGlob_WhenRootNotMounted()
    {
        Assert.Single(Ok(await GlobAsync("src/b.cs", "/workspace/proj")).Paths);

        foreach (var path in new[] { "/elsewhere/proj", "/workspace/missing", "/workspace/proj/src/b.cs" })
        {
            var r = await GlobAsync("**/*.cs", path);
            Assert.False(r.Success, path);
            Assert.False(string.IsNullOrWhiteSpace(r.Error), path);
            Assert.False(ReturnsPaths(r), path);
        }
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGlob_WhenPathOutsideAgentRoot()
    {
        await using var scope = AgentRunScope.Begin("agent-1", "reader", "/workspace/job");

        var inside = Ok(await GlobAsync("*.cs", "/workspace/job"));
        var outside = await GlobAsync("*.cs", "/workspace/other");

        Assert.Equal(["/workspace/job/x.cs"], inside.Paths);
        Assert.False(outside.Success);
        Assert.False(ReturnsPaths(outside));
        Assert.DoesNotContain(Path.GetTempPath(), outside.Error);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnTheFile_WhenWildcardFreePatternNamesAnExistingFile()
    {
        var res = Ok(await GlobAsync("src/b.cs", "/workspace/proj"));

        Assert.Equal(["/workspace/proj/src/b.cs"], res.Paths);
        Assert.False(res.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnNoPath_WhenWildcardFreePatternNamesNothing()
    {
        Assert.Single(Ok(await GlobAsync("src/b.cs", "/workspace/proj")).Paths);

        var res = Ok(await GlobAsync("src/none.cs", "/workspace/proj"));

        Assert.Empty(res.Paths);
        Assert.False(res.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldRefuseGlob_WhenPatternIsAbsolute()
    {
        Assert.Equal(3, Ok(await GlobAsync("**/*.cs", "/workspace/proj")).Paths.Count);

        var r = await GlobAsync("/workspace/proj/**/*.cs", "/workspace/proj");

        Assert.False(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.False(ReturnsPaths(r));
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public void ShouldBeReadAccess_WhenAccessAndSchedulerQueried()
    {
        Assert.Equal(ToolAccess.Read, _tool.Access);
        Assert.True(ToolCallScheduler.IsParallelRead("glob", ToolAccess.Read));
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnWholePathsOnly_WhenResponseExceedsTotalCharacterBound()
    {
        var dir = "/workspace/big/" + new string('d', 240);
        var all = new List<string>();
        for (var i = 0; i < 600; i++)
        {
            var p = $"{dir}/f{i:D4}.cs";
            Add(p);
            all.Add(p);
        }
        all.Sort(StringComparer.Ordinal);

        var r = await GlobAsync("**/*.cs", "/workspace/big", 1000);
        var res = Ok(r);

        Assert.True(res.Truncated);
        Assert.True(r.AlreadyBounded);
        Assert.NotEmpty(res.Paths);
        Assert.True(res.Paths.Count < all.Count);
        Assert.Equal(all.Take(res.Paths.Count), res.Paths);
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public void ShouldResolveGlobTool_WhenFileSystemToolsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(_fs);
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystemTools();
        using var provider = services.BuildServiceProvider();

        var tool = provider.GetServices<IBaseTool>().SingleOrDefault(t => t.Name == "glob");

        Assert.IsType<GlobTool>(tool);
    }

    [Theory]
    [Trait("BR", "RT-09b")]
    [InlineData("../other/y.cs", true)]
    [InlineData("src/../../other/y.cs", true)]
    [InlineData("..\\other\\y.cs", true)]
    [InlineData("*.cs", false)]
    [InlineData("x.cs", false)]
    [InlineData("a..b.txt", false)]
    public async Task ShouldRefuseGlob_WhenPatternHasParentSegment(string pattern, bool refused)
    {
        Add("/workspace/job/a..b.txt");
        await using var scope = AgentRunScope.Begin("agent-1", "reader", "/workspace/job");

        var r = await GlobAsync(pattern, "/workspace/job");

        if (refused)
        {
            Assert.False(r.Success);
            Assert.False(string.IsNullOrWhiteSpace(r.Error));
            Assert.False(ReturnsPaths(r));
        }
        else
        {
            Assert.NotEmpty(Ok(r).Paths);
        }
    }

    [Fact]
    [Trait("BR", "RT-09b")]
    public async Task ShouldReturnNoPath_WhenWildcardFreePatternNamesDirectory()
    {
        var res = Ok(await GlobAsync("src", "/workspace/proj"));

        Assert.Empty(res.Paths);
        Assert.False(res.Truncated);
    }
}
