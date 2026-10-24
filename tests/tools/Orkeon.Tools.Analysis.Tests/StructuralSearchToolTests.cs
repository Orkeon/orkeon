using System.Collections.Immutable;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class StructuralSearchToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("structsearch").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldReturnPathLineAndEnclosingSymbol_WhenSearchedThroughTool()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/src/A.cs", "class Svc { void Run() { Log.Write(\"hi\"); } }", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            Pattern = "Log.Write($A)",
            Language = "csharp",
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None);

        var hit = Assert.Single(resp.Matches);
        Assert.Equal("/workspace/src/A.cs", hit.Path);
        Assert.Equal(1, hit.Line);
        Assert.False(string.IsNullOrEmpty(hit.EnclosingSymbol));
        Assert.Contains("Run", hit.EnclosingSymbol);
        Assert.Equal("\"hi\"", hit.Captures["A"]);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldRefuseLanguage_WhenLanguageIsNotServed()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/src/a.py", "print(1)", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            Pattern = "print($A)",
            Language = "python",
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None));

        Assert.Contains("python", ex.Message);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldSetTruncated_WhenCandidatesExceedMaxFiles()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        foreach (var n in new[] { "A", "B", "C" })
            await fs.WriteAllTextAsync($"/workspace/src/{n}.cs", "class " + n + " { void Run() { Log.Write(\"x\"); } }", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var two = await tool.ExecuteTypedForTest(Request(2), CancellationToken.None);
        var three = await tool.ExecuteTypedForTest(Request(3), CancellationToken.None);

        Assert.True(two.Truncated);
        Assert.Equal(2, two.FilesScanned);
        Assert.False(three.Truncated);
        Assert.Equal(3, three.FilesScanned);
    }

    private static StructuralSearchRequest Request(int maxFiles) => new()
    {
        Pattern = "Log.Write($A)",
        Language = "csharp",
        Paths = ImmutableList.Create("/workspace/src"),
        MaxFiles = maxFiles,
    };

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldSearchWithRuleFile_WhenRuleFileGiven()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/rules/r.yml", "id: no-log\nlanguage: cs\nrule:\n  pattern: Log.Write($A)\n", CancellationToken.None);
        await fs.WriteAllTextAsync("/workspace/src/A.cs", "class Svc { void Run() { Log.Write(\"hi\"); Other(1); } }", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            RuleFile = "/workspace/rules/r.yml",
            Language = "csharp",
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None);

        var hit = Assert.Single(resp.Matches);
        Assert.Equal("/workspace/src/A.cs", hit.Path);
        Assert.Equal("Log.Write(\"hi\")", hit.Text);
    }
}
