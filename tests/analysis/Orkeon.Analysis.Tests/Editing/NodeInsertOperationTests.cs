using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class NodeInsertOperationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nodeinsert").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;

    public NodeInsertOperationTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/workspace");
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private async Task SeedAsync(string name, string content)
        => await _disk.WriteAllBytesAsync("/workspace/" + name, Encoding.UTF8.GetBytes(content), CancellationToken.None);

    private async Task<string> ReadAsync(string name)
        => Encoding.UTF8.GetString((await _disk.TryReadAllBytesAsync("/workspace/" + name, CancellationToken.None))!);

    private async Task<EditOutcome> InsertAsync(string name, string selector, InsertPosition position, string text)
    {
        var op = new NodeInsertOperation(NodeSelector.Parse(selector), position, text);
        return await new StructuralEditEngine(_disk, _pool).ApplyAsync("/workspace/" + name, [op]);
    }

    private const string TwoMembers =
        "public class C\n{\n    public int A() { return 1; }\n\n    public int B() { return 2; }\n}\n";

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldAppendMemberWithSiblingIndent_WhenInsertInsideType()
    {
        await SeedAsync("c.cs", TwoMembers);

        await InsertAsync("c.cs", "C", InsertPosition.Inside, "public int D()\n{\n    return 3;\n}");

        var result = await ReadAsync("c.cs");
        Assert.StartsWith("public class C\n{\n    public int A() { return 1; }\n\n    public int B() { return 2; }\n", result, StringComparison.Ordinal);
        Assert.EndsWith("    public int D()\n    {\n        return 3;\n    }\n}\n", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a.py", "class A:\n    def f(self):\n        pass\n\nx = 1\n", "A", "def g(self):\n    pass",
        "class A:\n    def f(self):\n        pass\n    def g(self):\n        pass\n\nx = 1\n")]
    [InlineData("a.rb", "class A\n  def f\n  end\nend\n", "A", "def g\nend",
        "class A\n  def f\n  end\n  def g\n  end\nend\n")]
    [InlineData("a.json", "{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", "a", "\"c\": 2",
        "{\n  \"a\": {\n    \"b\": 1,\n    \"c\": 2\n  }\n}\n")]
    [InlineData("a.rs", "enum E {\n    A\n}\n", "E", "B",
        "enum E {\n    A,\n    B\n}\n")]
    [Trait("BR", "CH-01")]
    public async Task ShouldAppendAfterTheLastMember_WhenBodyIsIndentedClosedByKeywordOrCommaSeparated(
        string name, string source, string selector, string text, string expected)
    {
        await SeedAsync(name, source);

        await InsertAsync(name, selector, InsertPosition.Inside, text);

        Assert.Equal(expected, await ReadAsync(name));
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldDetectTabs_WhenFileUsesTabs()
    {
        await SeedAsync("c.cs", "public class C\n{\n\tpublic int A() { return 1; }\n}\n");

        await InsertAsync("c.cs", "C", InsertPosition.Inside, "public int D()\n{\n\treturn 3;\n}");

        var result = await ReadAsync("c.cs");
        Assert.EndsWith("\tpublic int D()\n\t{\n\t\treturn 3;\n\t}\n}\n", result, StringComparison.Ordinal);
        Assert.DoesNotContain("    ", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldInsertIntoEmptyType_WhenTypeHasNoMember()
    {
        await SeedAsync("c.cs", "namespace N\n{\n    public class C\n    {\n    }\n}\n");

        await InsertAsync("c.cs", "N.C", InsertPosition.Inside, "public int D() { return 1; }");

        var result = await ReadAsync("c.cs");
        Assert.EndsWith("        public int D() { return 1; }\n    }\n}\n", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldPlaceBeforeAnchorAndKeepItsComments_WhenPositionIsBefore()
    {
        await SeedAsync("c.cs",
            "public class C\n{\n    /// <summary>A</summary>\n    [Obsolete]\n    public int A() { return 1; }\n}\n");

        await InsertAsync("c.cs", "C.A", InsertPosition.Before, "public int Z() { return 0; }");

        var result = await ReadAsync("c.cs");
        Assert.StartsWith("public class C\n{\n    public int Z() { return 0; }\n", result, StringComparison.Ordinal);
        Assert.EndsWith("    /// <summary>A</summary>\n    [Obsolete]\n    public int A() { return 1; }\n}\n", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldPlaceAfterAnchor_WhenPositionIsAfter()
    {
        await SeedAsync("c.cs", TwoMembers);

        await InsertAsync("c.cs", "C.A", InsertPosition.After, "public int Z() { return 0; }");

        var result = await ReadAsync("c.cs");
        var a = result.IndexOf("public int A() { return 1; }", StringComparison.Ordinal);
        var z = result.IndexOf("\n    public int Z() { return 0; }\n", StringComparison.Ordinal);
        var b = result.IndexOf("public int B() { return 2; }", StringComparison.Ordinal);
        Assert.True(a >= 0 && a < z && z < b, result);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldRefuseInsert_WhenResultDoesNotParse()
    {
        await SeedAsync("c.cs", TwoMembers);
        var before = await _disk.TryReadAllBytesAsync("/workspace/c.cs", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ParseBrokenException>(
            () => InsertAsync("c.cs", "C", InsertPosition.Inside, "public int D( {"));

        Assert.NotEmpty(ex.Diagnostics);
        Assert.Equal(before, await _disk.TryReadAllBytesAsync("/workspace/c.cs", CancellationToken.None));
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldAddSeparatingComma_WhenInsideEnum()
    {
        await SeedAsync("e.cs", "public enum E\n{\n    A,\n    B\n}\n");

        await InsertAsync("e.cs", "E", InsertPosition.Inside, "C");

        Assert.Equal("public enum E\n{\n    A,\n    B,\n    C\n}\n", await ReadAsync("e.cs"));
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldAppendAtSectionEnd_WhenInsideMarkdownHeading()
    {
        await SeedAsync("d.md", "# Title\n\nIntro\n\n## Usage\n\nuse it\n\n## Other\n\nmore\n");

        await InsertAsync("d.md", "Title/Usage", InsertPosition.Inside, "extra line");

        var result = await ReadAsync("d.md");
        var use = result.IndexOf("use it\n", StringComparison.Ordinal);
        var extra = result.IndexOf("extra line\n", StringComparison.Ordinal);
        var other = result.IndexOf("## Other\n", StringComparison.Ordinal);
        Assert.True(use >= 0 && use < extra && extra < other, result);
        Assert.EndsWith("## Other\n\nmore\n", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldInsertInsideTypeScriptClassBody_WhenLanguageIsTypeScript()
    {
        await SeedAsync("c.ts", "export class C {\n  a(): number { return 1; }\n}\n");

        await InsertAsync("c.ts", "C", InsertPosition.Inside, "b(): number { return 2; }");

        var result = await ReadAsync("c.ts");
        Assert.StartsWith("export class C {\n  a(): number { return 1; }\n", result, StringComparison.Ordinal);
        Assert.EndsWith("  b(): number { return 2; }\n}\n", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldReindentEveryLine_WhenTextIsMultiLineBefore()
    {
        await SeedAsync("c.cs", "public class C\n{\n    public int A() { return 1; }\n}\n");

        await InsertAsync("c.cs", "C.A", InsertPosition.Before, "public int Z()\n{\n    return 0;\n}");

        var result = await ReadAsync("c.cs");
        Assert.Contains("\n    public int Z()\n    {\n        return 0;\n    }\n", result, StringComparison.Ordinal);
        Assert.EndsWith("    public int A() { return 1; }\n}\n", result, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldUseFirstMemberIndent_WhenLastMemberSpansSeveralLines()
    {
        await SeedAsync("c.cs", "public class C\n{\n    public int Y() { return 1; }\n\n    public int X =>\n        42;\n}\n");

        await InsertAsync("c.cs", "C", InsertPosition.Inside, "public int D() { return 3; }");

        Assert.EndsWith("        42;\n    public int D() { return 3; }\n}\n", await ReadAsync("c.cs"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldPutClosingBraceOnItsOwnLine_WhenInsideSingleLineEmptyType()
    {
        await SeedAsync("c.cs", "class A {}\n");

        await InsertAsync("c.cs", "A", InsertPosition.Inside, "public int D() { return 3; }");

        var result = await ReadAsync("c.cs");
        Assert.Equal("class A {\n    public int D() { return 3; }\n}\n", result);
    }
}
