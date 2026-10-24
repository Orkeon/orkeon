using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class StructuralEditEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("structedit").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _fs;

    public StructuralEditEngineTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/workspace");
        _fs = new FakeCountingFileSystemService(_disk);
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private async Task<byte[]> SeedAsync(string name, byte[] content)
    {
        await _disk.WriteAllBytesAsync("/workspace/" + name, content, CancellationToken.None);
        return content;
    }

    private async Task<StructuralNode> ResolveAsync(string name, string selector)
    {
        var file = await StructuralFile.LoadAsync(_disk, _pool, "/workspace/" + name);
        return file.Resolve(NodeSelector.Parse(selector));
    }

    private async Task<EditOutcome> ReplaceAsync(string name, string selector, string text)
    {
        var node = await ResolveAsync(name, selector);
        var op = new NodeReplaceOperation(NodeSelector.Parse(selector), node.Sha256, text);
        return await new StructuralEditEngine(_fs, _pool).ApplyAsync("/workspace/" + name, [op]);
    }

    private static byte[] Splice(byte[] original, NodeRange range, byte[] replacement) =>
        [.. original[..range.StartByte], .. replacement, .. original[range.EndByte..]];

    private const string TwoMethods =
        "public class C\n{\n    public int A() { return 1; }\n\n    public int B() { return 2; }\n}\n";

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldSpliceOnlyNodeBytes_WhenHashIsFresh()
    {
        var original = await SeedAsync("c.cs", Encoding.UTF8.GetBytes(TwoMethods));
        var node = await ResolveAsync("c.cs", "C.B");
        const string text = "public int B() { return 42; }";
        var replacement = Encoding.UTF8.GetBytes(text);

        var outcome = await ReplaceAsync("c.cs", "C.B", text);

        var write = Assert.Single(_fs.Writes, w => w.Path == "/workspace/c.cs");
        Assert.Equal(Splice(original, node.Range, replacement), write.Bytes);
        Assert.Equal(node.Range.StartByte, outcome.Range.StartByte);
        Assert.Equal(node.Range.StartByte + replacement.Length, outcome.Range.EndByte);
        Assert.Equal(Sha(replacement), outcome.NewHash);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldPreserveBomAndLineEndings_WhenFileHasCrLf()
    {
        var original = await SeedAsync("c.cs",
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(TwoMethods.Replace("\n", "\r\n"))]);
        var node = await ResolveAsync("c.cs", "C.B");
        const string text = "public int B()\n{\n    return 42;\n}";
        var replacement = Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n"));

        await ReplaceAsync("c.cs", "C.B", text);

        var write = Assert.Single(_fs.Writes, w => w.Path == "/workspace/c.cs");
        Assert.Equal(Splice(original, node.Range, replacement), write.Bytes);
        Assert.True(write.Bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldRefuseWithParserError_WhenReplacementBreaksParse()
    {
        var original = await SeedAsync("c.cs", Encoding.UTF8.GetBytes(TwoMethods));

        var ex = await Assert.ThrowsAsync<ParseBrokenException>(
            () => ReplaceAsync("c.cs", "C.B", "public int B() { return 2;"));

        Assert.NotEmpty(ex.Diagnostics);
        Assert.Matches("Unexpected token|Missing node", string.Join("\n", ex.Diagnostics));
        Assert.DoesNotContain(_fs.Writes, w => w.Path == "/workspace/c.cs");
        Assert.Equal(original, await _disk.TryReadAllBytesAsync("/workspace/c.cs", CancellationToken.None));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldAcceptReplacement_WhenFileHadPreExistingErrorsAndCountDoesNotGrow()
    {
        const string broken = "public class C\n{\n    public int A() { return 1 +; }\n\n    public int B() { return 2; }\n}\n";
        var original = await SeedAsync("c.cs", Encoding.UTF8.GetBytes(broken));
        var node = await ResolveAsync("c.cs", "C.B");
        const string text = "public int B() { return 42; }";

        await ReplaceAsync("c.cs", "C.B", text);

        var write = Assert.Single(_fs.Writes, w => w.Path == "/workspace/c.cs");
        Assert.Equal(Splice(original, node.Range, Encoding.UTF8.GetBytes(text)), write.Bytes);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldRefuse_WhenReplacementFixesOneErrorButIntroducesAnother()
    {
        const string broken = "public class C\n{\n    public int A() { return 1; }\n\n    public int B() { return 2 +; }\n}\n";
        var original = await SeedAsync("c.cs", Encoding.UTF8.GetBytes(broken));

        var ex = await Assert.ThrowsAsync<ParseBrokenException>(
            () => ReplaceAsync("c.cs", "C.B", "public int B() { return (2; }"));

        Assert.NotEmpty(ex.Diagnostics);
        Assert.DoesNotContain(_fs.Writes, w => w.Path == "/workspace/c.cs");
        Assert.Equal(original, await _disk.TryReadAllBytesAsync("/workspace/c.cs", CancellationToken.None));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldRestoreOriginalBytes_WhenWriteFailsMidway()
    {
        var original = await SeedAsync("c.cs", Encoding.UTF8.GetBytes(TwoMethods));
        _fs.WriteFailure = new InvalidOperationException("disk exploded");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReplaceAsync("c.cs", "C.B", "public int B() { return 42; }"));

        Assert.Equal(original, await _disk.TryReadAllBytesAsync("/workspace/c.cs", CancellationToken.None));
        Assert.False(await _disk.ExistsAsync("/workspace/c.cs.orkeon-edit.bak", CancellationToken.None));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldKeepBackupAndReportBothFailures_WhenWriteAndRestoreCopyBothFail()
    {
        await SeedAsync("c.cs", Encoding.UTF8.GetBytes(TwoMethods));
        var writeFailure = new InvalidOperationException("disk exploded");
        var restoreFailure = new IOException("restore refused");
        _fs.WriteFailure = writeFailure;
        _fs.RestoreCopyFailure = restoreFailure;

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => ReplaceAsync("c.cs", "C.B", "public int B() { return 42; }"));

        Assert.Contains(writeFailure, ex.InnerExceptions);
        Assert.Contains(restoreFailure, ex.InnerExceptions);
        Assert.True(await _disk.ExistsAsync("/workspace/c.cs.orkeon-edit.bak", CancellationToken.None));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldSkipParseCheck_WhenFileIsMarkdown()
    {
        var original = await SeedAsync("d.md", Encoding.UTF8.GetBytes("# A\ntext\n\n# B\nbody\n"));
        var node = await ResolveAsync("d.md", "A");
        const string text = "{{{ ((( fn( ]]] ;;";

        await ReplaceAsync("d.md", "A", text);

        var write = Assert.Single(_fs.Writes, w => w.Path == "/workspace/d.md");
        Assert.Equal(Splice(original, node.Range, Encoding.UTF8.GetBytes(text)), write.Bytes);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldReplaceSectionWithItsSubSections_WhenMarkdownSelectorNamesParentHeading()
    {
        await SeedAsync("d.md", Encoding.UTF8.GetBytes("# A\nintro\n## A1\nsub\n# B\nbody\n"));

        await ReplaceAsync("d.md", "A", "# A\nnew");

        var write = Assert.Single(_fs.Writes, w => w.Path == "/workspace/d.md");
        Assert.Equal("# A\nnew\n# B\nbody\n", Encoding.UTF8.GetString(write.Bytes));
    }
}
