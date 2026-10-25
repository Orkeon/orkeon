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

    [Theory]
    [InlineData("python", "a.py", "class Svc:\n    def run(self):\n        log(1)\n", "log($A)", "Svc.run()")]
    [InlineData("go", "a.go", "package p\n\nfunc (s *Svc) Run() {\n\tlog(1)\n}\n", "log($A)", "Svc.Run()")]
    [InlineData("rust", "a.rs", "impl Svc {\n    fn run(&self) { log(1); }\n}\n", "log($A)", "Svc.run()")]
    [InlineData("java", "A.java", "class Svc { void run() { log(1); } }\n", "log($A)", "Svc.run()")]
    [InlineData("js", "a.js", "class Svc { run() { log(1); } }\n", "log($A)", "Svc.run()")]
    [InlineData("c", "a.c", "void run(void) { log(1); }\n", "log($A)", "run")]
    [InlineData("cpp", "a.cpp", "namespace n { void run() { log(1); } }\n", "log($A)", "n.run")]
    [InlineData("php", "a.php", "<?php\nclass Svc { function run() { log(1); } }\n", "log($A)", "Svc.run()")]
    [InlineData("ruby", "a.rb", "class Svc\n  def run\n    log(1)\n  end\nend\n", "log($A)", "Svc.run")]
    [InlineData("bash", "a.sh", "run() {\n  log 1\n}\n", "log $A", "run")]
    [InlineData("scala", "A.scala", "object Svc { def run() = log(1) }\n", "log($A)", "Svc.run()")]
    [InlineData("tsx", "a.tsx", "export function Run() { log(1); return <p/>; }\n", "log($A)", "Run()")]
    [Trait("BR", "CH-01")]
    public async Task ShouldMatchAndNameTheEnclosingSymbol_WhenLanguageIsAnyGrammar(
        string language, string file, string source, string pattern, string enclosing)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/src/" + file, source, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            Pattern = pattern,
            Language = language,
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None);

        var hit = Assert.Single(resp.Matches);
        Assert.Equal("1", hit.Captures["A"]);
        Assert.Equal(enclosing, hit.EnclosingSymbol);
    }

    [Theory]
    [InlineData("json", "a.json", "{\"a\": {\"b\": 1}}\n", "{\"b\": $A}")]
    [InlineData("css", "a.css", "a { color: red; }\n", "color: $A;")]
    [InlineData("html", "a.html", "<p>hi</p>\n", "<p>$A</p>")]
    [InlineData("julia", "a.jl", "function f(x)\n  g(1)\nend\n", "g($A)")]
    [Trait("BR", "CH-01")]
    public async Task ShouldMatch_WhenLanguageIsParseOnly(string language, string file, string source, string pattern)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/src/" + file, source, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            Pattern = pattern,
            Language = language,
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None);

        Assert.NotEmpty(resp.Matches);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldRefuseLanguage_WhenLanguageClaimsNoFile()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        using var pool = new TreeSitterParserPool();
        using var tool = new StructuralSearchTool(fs, pool);

        var ex = await Assert.ThrowsAnyAsync<ArgumentException>(() => tool.ExecuteTypedForTest(new StructuralSearchRequest
        {
            Pattern = "@param $A",
            Language = "jsdoc",
            Paths = ImmutableList.Create("/workspace/src"),
        }, CancellationToken.None));

        Assert.Contains("jsdoc", ex.Message);
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
