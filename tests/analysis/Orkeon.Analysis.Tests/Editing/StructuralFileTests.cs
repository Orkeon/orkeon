using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class StructuralFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("structfile").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldRefuseByName_WhenExtensionHasNoGrammar()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/x.xyz", "hello", CancellationToken.None);
        using var pool = new TreeSitterParserPool();

        var ex = await Assert.ThrowsAsync<UnsupportedExtensionException>(
            () => StructuralFile.LoadAsync(fs, pool, "/workspace/x.xyz"));

        Assert.Equal(".xyz", ex.Extension);
        Assert.Contains(".xyz", ex.Message);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public void ShouldPublishShippedGrammars_WhenListed()
    {
        var shipped = LanguageRegistry.Shipped;

        Assert.Contains(shipped, g => g.Language == "csharp" && g.Extensions.Contains(".cs"));
        Assert.Contains(shipped, g => g.Language == "typescript" && g.Extensions.Contains(".ts"));
        Assert.Contains(shipped, g => g.Extensions.Contains(".md"));
    }

    private const string Doc = "# A\nintro\n## B\nbody b\n### B1\nsub\n## C\nbody c\n# D\nbody d\n";
    private const string Section = "## B\nbody b\n### B1\nsub";

    private static string Sha(byte[] raw, int start, int end) =>
        Convert.ToHexString(SHA256.HashData(raw.AsSpan(start, end - start))).ToLowerInvariant();

    private async Task<StructuralFile> LoadBytesAsync(string name, byte[] content)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllBytesAsync("/workspace/" + name, content, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        return await StructuralFile.LoadAsync(fs, pool, "/workspace/" + name);
    }

    public static TheoryData<string, string, string[]> Outlines { get; } = new()
    {
        { "a.py", "import os\n\n@dec\nclass A(B):\n    def f(self, x: int):\n        pass\n\ndef g():\n    pass\n", ["A", "A.f(int)", "g()"] },
        { "a.go", "package p\n\ntype T struct{ a int }\n\nfunc (t *T) M(x int) int { return x }\n\nfunc F() {}\n", ["T", "T.M(int)", "F()"] },
        { "a.rs", "mod m {\n    pub fn f(x: i32) {}\n}\nstruct S;\nimpl S {\n    fn g(&self) {}\n}\ntrait T { fn h(&self); }\n", ["m", "m.f(i32)", "S", "S.g()", "T", "T.h()"] },
        { "A.java", "package p;\nclass A {\n  void f(int x) {}\n  A() {}\n}\ninterface I { void g(); }\n", ["A", "A.f(int)", "A.A()", "I", "I.g()"] },
        { "a.js", "class A {\n  f(x) {}\n}\nexport function g() {}\n", ["A", "A.f(any)", "g()"] },
        { "a.c", "struct S { int a; };\nstatic int f(int a) { return a; }\nint *g(void) { return 0; }\n", ["S", "f", "g"] },
        { "a.cpp", "namespace n {\nclass A {\npublic:\n  int f(int x) { return x; }\n};\ntemplate <typename T> T id(T x) { return x; }\n}\nint n::A::g() { return 1; }\n", ["n", "n.A", "n.A.f", "n.id", "n::A::g"] },
        { "a.php", "<?php\nclass A {\n  public function f($x) {}\n}\nfunction g() {}\n", ["A", "A.f(any)", "g()"] },
        { "a.rb", "module M\n  class A < B\n    def f(x)\n    end\n    def self.g; end\n  end\nend\n", ["M", "M.A", "M.A.f(any)", "M.A.g"] },
        { "a.sh", "f() { echo 1; }\nfunction g { echo 2; }\n", ["f", "g"] },
        { "A.scala", "object O {\n  def f(x: Int): Int = x\n}\nclass C { def g = 1 }\ntrait T\n", ["O", "O.f(Int)", "C", "C.g", "T"] },
        { "a.json", "{\"a\": {\"b\": 1}, \"c\": [1]}\n", ["a", "a.b", "c"] },
        { "a.jl", "module M\nfunction f(x)\n  x\nend\nend\n", ["M", "M.f"] },
        { "a.tsx", "export function A() { return <div>hi</div>; }\n", ["A()"] },
    };

    [Theory]
    [MemberData(nameof(Outlines))]
    [Trait("BR", "CH-01")]
    public async Task ShouldOutlineDeclarations_WhenFileIsInAnyIndexedOrNamedGrammar(string name, string source, string[] selectors)
    {
        var file = await LoadBytesAsync(name, Encoding.UTF8.GetBytes(source));

        Assert.Equal(selectors, file.Outline().Select(n => n.Selector).ToArray());
        Assert.All(selectors, s => Assert.False(string.IsNullOrEmpty(file.Resolve(NodeSelector.Parse(s)).Text)));
    }

    [Theory]
    [InlineData("a.css", "a { color: red; }\n@media screen { .b { x: y; } }\n", "@ts:stylesheet/0/rule_set", "a { color: red; }")]
    [InlineData("a.html", "<html><head></head><body><p>hi</p></body></html>\n", "@ts:document/0/element/2/element", "<body><p>hi</p></body>")]
    [InlineData("A.hs", "module M where\nf :: Int -> Int\nf x = x\n", "@ts:haskell/0/header", "module M where")]
    [Trait("BR", "CH-01")]
    public async Task ShouldOutlineTopLevelNodesByPath_WhenGrammarHasNoDeclarationKinds(string name, string source, string selector, string text)
    {
        var file = await LoadBytesAsync(name, Encoding.UTF8.GetBytes(source));

        Assert.Contains(selector, file.Outline().Select(n => n.Selector));
        Assert.Equal(text, file.Resolve(NodeSelector.Parse(selector)).Text);
    }

    [Theory]
    [InlineData(".agda")]
    [InlineData(".erb")]
    [InlineData(".ql")]
    [InlineData(".razor")]
    [InlineData(".v")]
    [Trait("BR", "CH-01")]
    public async Task ShouldLoad_WhenExtensionBelongsToAParseOnlyGrammar(string extension)
    {
        var file = await LoadBytesAsync("x" + extension, Encoding.UTF8.GetBytes("x\n"));

        Assert.NotNull(file.Outline());
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public void ShouldClaimEachExtensionOnce_WhenGrammarsAreParseable()
    {
        var extensions = LanguageRegistry.Parseable.SelectMany(g => g.Extensions).ToList();

        Assert.Equal(extensions.Count, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(LanguageRegistry.Parseable, g => Assert.True(g.Language == "markdown" || LanguageRegistry.IsSupported(g.Language), g.Language));
        Assert.Contains(LanguageRegistry.Parseable, g => g.Language == "tsx" && g.Extensions.SequenceEqual([".tsx"]));
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldKeepBomAndCrlfBytes_WhenHashingNode()
    {
        const string text = "namespace N\r\n{\r\n    public class C\r\n    {\r\n        public int M(int a)\r\n        {\r\n            return a;\r\n        }\r\n    }\r\n}\r\n";
        var raw = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var file = await LoadBytesAsync("Bom.cs", raw);

        var node = file.Resolve(NodeSelector.Parse("N.C.M(int)"));

        var expected = Encoding.UTF8.GetBytes("public int M(int a)\r\n        {\r\n            return a;\r\n        }");
        var start = raw.AsSpan().IndexOf(expected);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + expected.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + expected.Length), node.Sha256);
        Assert.Equal(Encoding.UTF8.GetString(expected), node.Text);
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldConvertOffsetsToBytes_WhenLeadingCommentHasMultiByteCharacter()
    {
        const string member = "// café € éé €€\n        public int M(int a)\n        {\n            return a;\n        }";
        const string text = "namespace N\n{\n    public class C\n    {\n        " + member + "\n    }\n}\n";
        var raw = Encoding.UTF8.GetBytes(text);
        var file = await LoadBytesAsync("Multi.cs", raw);

        var node = file.Resolve(NodeSelector.Parse("N.C.M(int)"));

        var expected = Encoding.UTF8.GetBytes(member);
        var start = raw.AsSpan().IndexOf(expected);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + expected.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + expected.Length), node.Sha256);
        Assert.Equal(member, node.Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldOutlineMarkdownHeadings_WhenFileIsMarkdown()
    {
        var file = await LoadBytesAsync("d.md", Encoding.UTF8.GetBytes(Doc));

        var outline = file.Outline();

        Assert.Equal(["A", "A/B", "A/B/B1", "A/C", "D"], outline.Select(n => n.Selector).ToArray());
        var b = outline.Single(n => n.Selector == "A/B");
        var start = Doc.IndexOf(Section, StringComparison.Ordinal);
        Assert.Equal(start, b.Range.StartByte);
        Assert.Equal(start + Section.Length, b.Range.EndByte);
        Assert.Equal(3, b.Range.StartLine);
        Assert.Equal(6, b.Range.EndLine);
        var a = outline.Single(n => n.Selector == "A");
        var b1 = outline.Single(n => n.Selector == "A/B/B1");
        Assert.Equal(5, b1.Range.StartLine);
        Assert.Equal(6, b1.Range.EndLine);
        Assert.Equal(1, a.Range.StartLine);
        Assert.Equal(8, a.Range.EndLine);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSectionPath_WhenSelectorIsMarkdownPath()
    {
        var raw = Encoding.UTF8.GetBytes(Doc);
        var file = await LoadBytesAsync("d.md", raw);

        var node = file.Resolve(NodeSelector.Parse("A/B"));

        var start = Doc.IndexOf(Section, StringComparison.Ordinal);
        Assert.Equal(Section, node.Text);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + Section.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + Section.Length), node.Sha256);

        var ex = Assert.Throws<SelectorNotFoundException>(() => file.Resolve(NodeSelector.Parse("A/Z")));
        Assert.Contains("A/B", ex.Suggestions);
    }

    private const string Dup = "# A\n## Notes\nfirst\n## Notes\nsecond\n";

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldThrowAmbiguousWithOrdinalCandidates_WhenMarkdownSiblingsShareName()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        var ex = Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("A/Notes")));

        Assert.Equal(["A/Notes[1]", "A/Notes[2]"], ex.Candidates.ToArray());
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSecondSection_WhenOrdinalSelectorGiven()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("A/Notes[1]")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("A/Notes[2]")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveEachWithoutOrdinal_WhenSameNameUnderDifferentParents()
    {
        var file = await LoadBytesAsync("two.md", Encoding.UTF8.GetBytes("# A\n## Notes\nin a\n# B\n## Notes\nin b\n"));

        Assert.Equal("## Notes\nin a", file.Resolve(NodeSelector.Parse("A/Notes")).Text);
        Assert.Equal("## Notes\nin b", file.Resolve(NodeSelector.Parse("B/Notes")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSingleHeading_WhenNameIsUnique()
    {
        var file = await LoadBytesAsync("d.md", Encoding.UTF8.GetBytes(Doc));

        Assert.Equal(Section, file.Resolve(NodeSelector.Parse("A/B")).Text);
        Assert.Equal(Section, file.Resolve(NodeSelector.Parse("A/B[1]")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldListDuplicatesWithOrdinal_WhenOutlineHasSiblingsOfSameName()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["A", "A/Notes[1]", "A/Notes[2]"], selectors);
        foreach (var selector in selectors)
            Assert.Equal(selector, file.Resolve(NodeSelector.Parse(selector)).Selector);
    }

    [Theory]
    [InlineData("A/Notes[3]")]
    [InlineData("A/Notes[0]")]
    [Trait("BR", "RT-44")]
    public async Task ShouldReportNotFound_WhenOrdinalExceedsSiblings(string selector)
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        Assert.Throws<SelectorNotFoundException>(() => file.Resolve(NodeSelector.Parse(selector)));
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveEachChildUnderSlashHeading_WhenHeadingContainsSlash()
    {
        var file = await LoadBytesAsync("ci.md", Encoding.UTF8.GetBytes("# CI/CD\n## Build\nb\n## Deploy\nd\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["CI/CD", "CI/CD/Build", "CI/CD/Deploy"], selectors);
        Assert.Equal("## Build\nb", file.Resolve(NodeSelector.Parse("CI/CD/Build")).Text);
        Assert.Equal("## Deploy\nd", file.Resolve(NodeSelector.Parse("CI/CD/Deploy")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldTakeOrdinals_WhenDuplicateSectionsSitUnderSlashHeading()
    {
        var file = await LoadBytesAsync("cin.md", Encoding.UTF8.GetBytes("# CI/CD\n## Notes\nfirst\n## Notes\nsecond\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["CI/CD", "CI/CD/Notes[1]", "CI/CD/Notes[2]"], selectors);
        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("CI/CD/Notes[1]")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("CI/CD/Notes[2]")).Text);
        var ex = Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("CI/CD/Notes")));
        Assert.Equal(["CI/CD/Notes[1]", "CI/CD/Notes[2]"], ex.Candidates.ToArray());
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldOutlineAndResolveOrdinalOnParent_WhenParentHeadingIsDuplicated()
    {
        var file = await LoadBytesAsync("pp.md", Encoding.UTF8.GetBytes("# A\n## Notes\nfirst\n# A\n## Notes\nsecond\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["A[1]", "A[1]/Notes", "A[2]", "A[2]/Notes"], selectors);
        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("A[1]/Notes")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("A[2]/Notes")).Text);
        Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("A/Notes")));
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveLiteralHeading_WhenHeadingNameLooksLikeOrdinal()
    {
        var literal = await LoadBytesAsync("lit.md", Encoding.UTF8.GetBytes("# A\n## x[2]\nlit\n"));

        Assert.Equal(["A", "A/x[2]"], literal.Outline().Select(n => n.Selector).ToArray());
        Assert.Equal("## x[2]\nlit", literal.Resolve(NodeSelector.Parse("A/x[2]")).Text);

        var plain = await LoadBytesAsync("ord.md", Encoding.UTF8.GetBytes("# A\n## x\none\n## x\ntwo\n"));

        Assert.Equal("## x\ntwo", plain.Resolve(NodeSelector.Parse("A/x[2]")).Text);
    }
}
