using System.Collections.Immutable;
using System.Text;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class StructuralRewriteToolTests : IDisposable
{
    private const string Pattern = "Log.Write($A)";
    private const string Fix = "Log.Info($A)";
    private const string StatementFix = "int z = $A;";
    private const string SrcA = "class A\n{\n    void Run()\n    {\n        Log.Write(1);\n    }\n}\n";
    private const string SrcB = "class B\n{\n    void Run()\n    {\n        Run(Log.Write(1));\n    }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("structrewrite").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly FakeIndexInvalidation _index = new();

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private string Disk(string name) => System.IO.Path.Combine(_dir, "src", name);

    private async Task<DiskBackedFileSystemService> SeedAsync(params (string Name, string Text)[] files)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        foreach (var (name, text) in files)
            await fs.WriteAllTextAsync("/workspace/src/" + name, text, CancellationToken.None);
        return fs;
    }

    private static StructuralRewriteRequest Request(string fix = Fix, bool dryRun = true) => new()
    {
        Pattern = Pattern,
        Language = "csharp",
        Fix = fix,
        Paths = ImmutableList.Create("/workspace/src"),
        DryRun = dryRun,
    };

    private Task<byte[]> BytesAsync(string name) => File.ReadAllBytesAsync(Disk(name), TestContext.Current.CancellationToken);

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldReturnUnifiedDiffPerFileAndWriteNothing_WhenDryRun()
    {
        var srcB = SrcA.Replace("class A", "class B", StringComparison.Ordinal);
        var fs = await SeedAsync(("A.cs", SrcA), ("B.cs", srcB));
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(Request(), CancellationToken.None);

        Assert.Equal(["/workspace/src/A.cs", "/workspace/src/B.cs"], resp.Files.Select(f => f.Path).Order().ToArray());
        foreach (var file in resp.Files)
        {
            Assert.Equal(1, file.Matches);
            Assert.Equal(0, file.Applied);
            Assert.Null(file.RefusedReason);
            Assert.Contains("@@", file.Diff, StringComparison.Ordinal);
            Assert.Contains("-        Log.Write(1);", file.Diff, StringComparison.Ordinal);
            Assert.Contains("+        Log.Info(1);", file.Diff, StringComparison.Ordinal);
        }
        Assert.Equal(Encoding.UTF8.GetBytes(SrcA), await BytesAsync("A.cs"));
        Assert.Equal(Encoding.UTF8.GetBytes(srcB), await BytesAsync("B.cs"));
        Assert.Empty(_index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldWriteOnlyMatchedRanges_WhenApplied()
    {
        const string original = "// keep  me\r\nclass A\r\n{\r\n    void Run()\r\n    {\r\n        Log.Write(1);\r\n        var k = 2;   \r\n        Log.Write(\"x\");\r\n    }\r\n}\r\n";
        const string expected = "// keep  me\r\nclass A\r\n{\r\n    void Run()\r\n    {\r\n        Log.Info(1);\r\n        var k = 2;   \r\n        Log.Info(\"x\");\r\n    }\r\n}\r\n";
        var srcB = SrcA.Replace("class A", "class B", StringComparison.Ordinal);
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllBytesAsync("/workspace/src/A.cs", Encoding.UTF8.GetBytes(original), CancellationToken.None);
        await fs.WriteAllTextAsync("/workspace/src/B.cs", srcB, CancellationToken.None);
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(Request(dryRun: false), CancellationToken.None);

        var a = Assert.Single(resp.Files, f => f.Path == "/workspace/src/A.cs");
        Assert.Equal(2, a.Applied);
        Assert.Equal(2, a.Matches);
        Assert.Null(a.RefusedReason);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), await BytesAsync("A.cs"));
        Assert.Equal(Encoding.UTF8.GetBytes(srcB.Replace("Log.Write", "Log.Info", StringComparison.Ordinal)), await BytesAsync("B.cs"));
        Assert.Equal(["/workspace/src/A.cs", "/workspace/src/B.cs"], _index.DirtyPaths.Order().ToArray());
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldRefuseFileAndLeaveItUntouched_WhenRewriteBreaksParse()
    {
        var fs = await SeedAsync(("B.cs", SrcB));
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(Request(StatementFix, dryRun: false), CancellationToken.None);

        var file = Assert.Single(resp.Files);
        Assert.Equal("/workspace/src/B.cs", file.Path);
        Assert.Equal(0, file.Applied);
        Assert.Contains("parse", file.RefusedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Encoding.UTF8.GetBytes(SrcB), await BytesAsync("B.cs"));
        Assert.Empty(_index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldStillWriteOtherFiles_WhenOneFileIsRefused()
    {
        var fs = await SeedAsync(("A.cs", SrcA), ("B.cs", SrcB));
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(Request(StatementFix, dryRun: false), CancellationToken.None);

        Assert.Equal(2, resp.Files.Count);
        var a = Assert.Single(resp.Files, f => f.Path == "/workspace/src/A.cs");
        var b = Assert.Single(resp.Files, f => f.Path == "/workspace/src/B.cs");
        Assert.Equal(1, a.Applied);
        Assert.Null(a.RefusedReason);
        Assert.NotNull(b.RefusedReason);
        Assert.Equal(Encoding.UTF8.GetBytes(SrcA.Replace("Log.Write(1);", "int z = 1;;", StringComparison.Ordinal)), await BytesAsync("A.cs"));
        Assert.Equal(Encoding.UTF8.GetBytes(SrcB), await BytesAsync("B.cs"));
        Assert.Equal(["/workspace/src/A.cs"], _index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldRefuseByName_WhenNoFixIsResolved()
    {
        var fs = await SeedAsync(("A.cs", SrcA));
        await fs.WriteAllTextAsync("/workspace/rules/nofix.yml", "id: r\nlanguage: cs\nrule:\n  pattern: Log.Write($A)\n", CancellationToken.None);
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var inline = await Assert.ThrowsAnyAsync<Exception>(() => tool.ExecuteTypedForTest(
            new StructuralRewriteRequest { Pattern = Pattern, Language = "csharp", Paths = ImmutableList.Create("/workspace/src") }, CancellationToken.None));
        var ruleFile = await Assert.ThrowsAnyAsync<Exception>(() => tool.ExecuteTypedForTest(
            new StructuralRewriteRequest { RuleFile = "/workspace/rules/nofix.yml", Paths = ImmutableList.Create("/workspace/src") }, CancellationToken.None));

        Assert.Contains("'fix'", inline.Message, StringComparison.Ordinal);
        Assert.Contains("'fix'", ruleFile.Message, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(SrcA), await BytesAsync("A.cs"));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldAcceptInlinePatternLanguageAndFix_WhenNoRule()
    {
        var fs = await SeedAsync(("A.cs", SrcA));
        await fs.WriteAllTextAsync("/workspace/rules/r.yml", "id: r\nlanguage: cs\nrule:\n  pattern: Log.Write($A)\nfix: Log.Info($A)\n", CancellationToken.None);
        await fs.WriteAllTextAsync("/workspace/rules/other.yml", "id: r\nlanguage: cs\nrule:\n  pattern: Log.Write($A)\nfix: Log.Debug($A)\n", CancellationToken.None);
        using var tool = new StructuralRewriteTool(fs, _pool, _index);
        var paths = ImmutableList.Create("/workspace/src");

        var inline = await tool.ExecuteTypedForTest(Request(), CancellationToken.None);
        var byRule = await tool.ExecuteTypedForTest(new StructuralRewriteRequest { RuleFile = "/workspace/rules/r.yml", Paths = paths }, CancellationToken.None);
        var overridden = await tool.ExecuteTypedForTest(
            new StructuralRewriteRequest { RuleFile = "/workspace/rules/other.yml", Fix = Fix, Paths = paths }, CancellationToken.None);

        var diff = Assert.Single(inline.Files).Diff;
        Assert.Contains("+        Log.Info(1);", diff, StringComparison.Ordinal);
        Assert.Equal(diff, Assert.Single(byRule.Files).Diff);
        Assert.Equal(diff, Assert.Single(overridden.Files).Diff);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldRefuseMarkdownByName_WhenRuleTargetsMarkdown()
    {
        var fs = await SeedAsync(("A.cs", SrcA));
        await fs.WriteAllTextAsync("/workspace/src/doc.md", "# Title\n\ntext\n", CancellationToken.None);
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => tool.ExecuteTypedForTest(
            new StructuralRewriteRequest { Pattern = "text", Language = "markdown", Fix = "other", Paths = ImmutableList.Create("/workspace/src"), DryRun = false },
            CancellationToken.None));

        Assert.Contains("markdown", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structural_rewrite", ex.Message, StringComparison.Ordinal);
        Assert.Equal("# Title\n\ntext\n", await File.ReadAllTextAsync(Disk("doc.md"), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldReportReadOnlyFileAsRefusedAndGoOn_WhenMountIsReadOnly()
    {
        var bytes = Encoding.UTF8.GetBytes(SrcA);
        var fs = new FakeRightsFileSystemService()
            .AddMount("/ro", FileAccessRights.Read)
            .AddMount("/rw", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create)
            .AddFile("/ro/A.cs", bytes)
            .AddFile("/rw/B.cs", bytes);
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(new StructuralRewriteRequest
        {
            Pattern = Pattern,
            Language = "csharp",
            Fix = Fix,
            Paths = ImmutableList.Create("/ro", "/rw"),
            DryRun = false,
        }, CancellationToken.None);

        var ro = Assert.Single(resp.Files, f => f.Path == "/ro/A.cs");
        var rw = Assert.Single(resp.Files, f => f.Path == "/rw/B.cs");
        Assert.Contains("does not grant the right", ro.RefusedReason, StringComparison.Ordinal);
        Assert.Equal(0, ro.Applied);
        Assert.Null(rw.RefusedReason);
        Assert.Equal(1, rw.Applied);
        Assert.Equal(bytes, await fs.TryReadAllBytesAsync("/ro/A.cs", TestContext.Current.CancellationToken));
        Assert.Equal(["/rw/B.cs"], _index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldRewriteWithMixedCaseMetavariable_WhenPatternUsesIt()
    {
        const string src = "class A\n{\n    void Run()\n    {\n        foo(x);\n    }\n}\n";
        var fs = await SeedAsync(("A.cs", src));
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(new StructuralRewriteRequest
        {
            Pattern = "foo($Name)",
            Language = "csharp",
            Fix = "bar($Name)",
            Paths = ImmutableList.Create("/workspace/src"),
            DryRun = true,
        }, CancellationToken.None);

        var file = Assert.Single(resp.Files);
        Assert.Contains("+        bar(x);", file.Diff, StringComparison.Ordinal);
        Assert.DoesNotContain("bar(ame)", file.Diff, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-32")]
    public async Task ShouldRestoreOriginalAndRefuse_WhenWriteFailsPartway()
    {
        var inner = await SeedAsync(("A.cs", SrcA));
        var fs = new FakeCountingFileSystemService(inner) { WriteFailure = new IOException("disk full") };
        using var tool = new StructuralRewriteTool(fs, _pool, _index);

        var resp = await tool.ExecuteTypedForTest(Request(dryRun: false), CancellationToken.None);

        var file = Assert.Single(resp.Files);
        Assert.NotNull(file.RefusedReason);
        Assert.Equal(0, file.Applied);
        Assert.Equal(Encoding.UTF8.GetBytes(SrcA), await BytesAsync("A.cs"));
    }
}
