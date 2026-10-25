using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public sealed class SourceSliceReaderTests
{
    private const string Path = "/src/Calc.cs";
    private const string Content = "line1\nline2\nline3\nline4\nline5\nline6";

    private readonly FakeCountingFileSystemService _fs = new(
        new FakeFileSystemService().AddMount("/src").AddFile(Path, Content));

    private static RaggableNode Node(
        int start, int end, string path = Path, string sha = "", string signature = "int Add()", string doc = "") => new()
    {
        Id = "n1",
        Kind = UniversalNodeKind.Method,
        Name = "Add",
        VirtualFilePath = path,
        Range = new NodeRange(0, 10, start, end, 0),
        Level = NodeLevel.L3_Symbol,
        Language = "csharp",
        SourceSnippet = "snapshot",
        Sha256 = sha,
        Signature = signature,
        DocComment = doc,
    };

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ShouldThrow_WhenArgumentsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SourceSliceReader(null!, 10));
        var reader = new SourceSliceReader(_fs, 10);
        Assert.Throws<ArgumentNullException>(() => { _ = reader.ReadAsync(null!, SourceMode.FullSpan, CancellationToken.None); });
    }

    [Fact]
    public async Task ShouldSliceFreshLinesAndMarkStable_WhenHashMatches()
    {
        var reader = new SourceSliceReader(_fs, 10);

        var slice = await reader.ReadAsync(Node(2, 4, sha: Sha("line2\nline3\nline4")), SourceMode.FullSpan, CancellationToken.None);

        Assert.Equal("line2\nline3\nline4", slice.Source);
        Assert.Equal(2, slice.StartLine);
        Assert.Equal(4, slice.EndLine);
        Assert.True(slice.Stable);
        Assert.Equal(Sha("line2\nline3\nline4"), slice.Sha256);
    }

    [Fact]
    public async Task ShouldMarkUnstable_WhenFreshHashDiffersFromIndexedOne()
    {
        var reader = new SourceSliceReader(_fs, 10);

        var slice = await reader.ReadAsync(Node(2, 3, sha: "deadbeef"), SourceMode.SignatureAndBody, CancellationToken.None);

        Assert.Equal("line2\nline3", slice.Source);
        Assert.False(slice.Stable);
    }

    [Fact]
    public async Task ShouldBoundBody_WhenSpanExceedsMaxLines()
    {
        var reader = new SourceSliceReader(_fs, 2);

        var body = await reader.ReadAsync(Node(1, 6), SourceMode.SignatureAndBody, CancellationToken.None);
        var statement = await reader.ReadAsync(Node(3, 6), SourceMode.StatementSpan, CancellationToken.None);

        Assert.Equal("line1\nline2", body.Source);
        Assert.Equal(2, body.EndLine);
        Assert.Equal("line3\nline4", statement.Source);
        Assert.Equal(6, statement.EndLine);
    }

    [Fact]
    public async Task ShouldClampRange_WhenLinesFallOutsideTheFile()
    {
        var reader = new SourceSliceReader(_fs, 50);

        var beyond = await reader.ReadAsync(Node(0, 0), SourceMode.FullSpan, CancellationToken.None);
        var tail = await reader.ReadAsync(Node(5, 99), SourceMode.FullSpan, CancellationToken.None);

        Assert.Equal("line1", beyond.Source);
        Assert.Equal(1, beyond.StartLine);
        Assert.Equal(1, beyond.EndLine);
        Assert.Equal("line5\nline6", tail.Source);
    }

    [Fact]
    public async Task ShouldReturnSignatureAndDoc_WhenSignatureModesRequested()
    {
        var reader = new SourceSliceReader(_fs, 10);

        var signature = await reader.ReadAsync(Node(2, 5), SourceMode.SignatureOnly, CancellationToken.None);
        var withDoc = await reader.ReadAsync(Node(2, 5, doc: "/// Adds."), SourceMode.SignatureAndDoc, CancellationToken.None);
        var noDoc = await reader.ReadAsync(Node(2, 5), SourceMode.SignatureAndDoc, CancellationToken.None);
        var empty = await reader.ReadAsync(Node(2, 5, signature: string.Empty), SourceMode.SignatureOnly, CancellationToken.None);

        Assert.Equal("int Add()", signature.Source);
        Assert.Equal(2, signature.EndLine);
        Assert.Equal("/// Adds.\nint Add()", withDoc.Source);
        Assert.Equal("int Add()", noDoc.Source);
        Assert.Equal(string.Empty, empty.Sha256);
        Assert.True(empty.Stable);
    }

    [Fact]
    public async Task ShouldReturnSnippet_WhenModeIsUnknown()
    {
        var reader = new SourceSliceReader(_fs, 10);

        var slice = await reader.ReadAsync(Node(2, 5), (SourceMode)99, CancellationToken.None);

        Assert.Equal("snapshot", slice.Source);
    }

    [Fact]
    public async Task ShouldFallBackToSnapshot_WhenFileIsMissingOrPathEmpty()
    {
        var reader = new SourceSliceReader(_fs, 10);

        var missing = await reader.ReadAsync(Node(2, 5, path: "/src/Gone.cs", sha: "abc"), SourceMode.FullSpan, CancellationToken.None);
        var noPath = await reader.ReadAsync(Node(2, 5, path: string.Empty), SourceMode.SignatureOnly, CancellationToken.None);

        Assert.Equal("snapshot", missing.Source);
        Assert.Equal("abc", missing.Sha256);
        Assert.False(missing.Stable);
        Assert.Equal(5, missing.EndLine);
        Assert.Equal("int Add()", noPath.Source);
        Assert.False(noPath.Stable);
    }

    [Fact]
    public async Task ShouldReadFileOnce_UntilCacheIsCleared()
    {
        var reader = new SourceSliceReader(_fs, 10);

        await reader.ReadAsync(Node(1, 1), SourceMode.FullSpan, CancellationToken.None);
        await reader.ReadAsync(Node(2, 2), SourceMode.FullSpan, CancellationToken.None);
        Assert.Single(_fs.Reads);

        reader.ClearCache();
        await reader.ReadAsync(Node(3, 3), SourceMode.FullSpan, CancellationToken.None);

        Assert.Equal(2, _fs.Reads.Count);
    }
}
