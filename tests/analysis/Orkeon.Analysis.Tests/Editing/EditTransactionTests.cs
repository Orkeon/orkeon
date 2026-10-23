using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class EditTransactionTests : IDisposable
{
    private const string TwoMethods =
        "public class C\n{\n    public int A() { return 1; }\n\n    public int B() { return 2; }\n}\n";

    private const string NewA = "public int A() { return 10; }";

    private readonly string _dir = Directory.CreateTempSubdirectory("edittx").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _fs;

    public EditTransactionTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/");
        _fs = new FakeCountingFileSystemService(_disk);
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Virtual(string name) => "/workspace/" + name;

    private string Physical(string name) => Path.Combine(_dir, "workspace", name);

    private async Task<byte[]> SeedAsync(string name, string content = TwoMethods)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await _disk.WriteAllBytesAsync(Virtual(name), bytes, CancellationToken.None);
        return bytes;
    }

    private Task<byte[]> OnDiskAsync(string name) => File.ReadAllBytesAsync(Physical(name), TestContext.Current.CancellationToken);

    private string[] JournalFiles()
    {
        var folder = Path.Combine(_dir, "output", "edit-batch");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.journal") : [];
    }

    private async Task<string> HashAsync(string name, string selector)
    {
        var file = await StructuralFile.LoadAsync(_disk, _pool, Virtual(name));
        return file.Resolve(NodeSelector.Parse(selector)).Sha256;
    }

    private static NodeReplaceOperation Replace(string selector, string hash, string text)
        => new(NodeSelector.Parse(selector), hash, text);

    private EditTransaction Transaction(FakeCountingFileSystemService fs, IBuildValidator? validator = null)
        => new(fs, _pool, validator);

    private async Task<(byte[] A, byte[] B)> LeaveJournalBehindAsync(InvalidOperationException writeFailure, IOException restoreFailure)
    {
        var a = await SeedAsync("a.cs");
        var b = await SeedAsync("b.cs");
        var hashA = await HashAsync("a.cs", "C.A");
        var hashB = await HashAsync("b.cs", "C.A");
        _fs.WriteOutcomesByPath[Virtual("a.cs")] = new Queue<Exception?>([null, restoreFailure]);
        _fs.WriteOutcomesByPath[Virtual("b.cs")] = new Queue<Exception?>([writeFailure]);
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.A", hashA, NewA));
        tx.Add(Virtual("b.cs"), Replace("C.A", hashB, NewA));
        await Assert.ThrowsAsync<AggregateException>(() => tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));
        return (a, b);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldRefuseStaleHash_WhenSecondOperationTargetsFirstOperationFile()
    {
        var original = await SeedAsync("c.cs");
        var originalHash = await HashAsync("c.cs", "C.A");
        var tx = Transaction(_fs);
        tx.Add(Virtual("c.cs"), Replace("C.A", originalHash, NewA));
        tx.Add(Virtual("c.cs"), Replace("C.A", originalHash, "public int A() { return 20; }"));

        await Assert.ThrowsAsync<StaleHashException>(() => tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));

        Assert.Equal(original, await OnDiskAsync("c.cs"));
        Assert.DoesNotContain(_fs.Writes, w => w.Path == Virtual("c.cs"));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldApplyOperationsInOrderOnProgressivelyEditedBytes_WhenSeveralOperationsHitOneFile()
    {
        await SeedAsync("c.cs");
        const string second = "public int A() { return 20; }";
        var tx = Transaction(_fs);
        tx.Add(Virtual("c.cs"), Replace("C.A", await HashAsync("c.cs", "C.A"), NewA));
        tx.Add(Virtual("c.cs"), Replace("C.A", Sha(NewA), second));

        var result = await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        Assert.Equal(TwoMethods.Replace("public int A() { return 1; }", second, StringComparison.Ordinal),
            Encoding.UTF8.GetString(await OnDiskAsync("c.cs")));
        var start = TwoMethods.IndexOf("public int A()", StringComparison.Ordinal);
        Assert.Equal(2, result.Operations.Count);
        Assert.Equal(Virtual("c.cs"), result.Operations[0].Path);
        Assert.Equal(new NodeRange(start, start + NewA.Length, 3, 3), result.Operations[0].Range);
        Assert.Equal(Virtual("c.cs"), result.Operations[1].Path);
        Assert.Equal(new NodeRange(start, start + second.Length, 3, 3), result.Operations[1].Range);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldCheckWriteRightOfEveryPath_BeforeAnyWrite()
    {
        var a = await SeedAsync("a.cs");
        var b = await SeedAsync("b.cs");
        var hashA = await HashAsync("a.cs", "C.A");
        var hashB = await HashAsync("b.cs", "C.A");
        _fs.DeniedWritePaths.Add(Virtual("b.cs"));
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.A", hashA, NewA));
        tx.Add(Virtual("b.cs"), Replace("C.A", hashB, NewA));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));

        Assert.Equal(a, await OnDiskAsync("a.cs"));
        Assert.Equal(b, await OnDiskAsync("b.cs"));
        Assert.DoesNotContain(_fs.Writes, w => w.Path is "/workspace/a.cs" or "/workspace/b.cs");
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldRestoreWrittenFilesFromMemory_WhenALaterWriteFails()
    {
        var a = await SeedAsync("a.cs");
        var b = await SeedAsync("b.cs");
        var hashA = await HashAsync("a.cs", "C.A");
        var hashB = await HashAsync("b.cs", "C.A");
        var writeFailure = new InvalidOperationException("disk exploded");
        _fs.WriteOutcomesByPath[Virtual("b.cs")] = new Queue<Exception?>([writeFailure]);
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.A", hashA, NewA));
        tx.Add(Virtual("b.cs"), Replace("C.A", hashB, NewA));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));

        Assert.Same(writeFailure, thrown);
        Assert.Equal(a, await OnDiskAsync("a.cs"));
        Assert.Equal(b, await OnDiskAsync("b.cs"));
        Assert.Empty(JournalFiles());
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldKeepJournalAndThrowAggregate_WhenRestoreFails()
    {
        await SeedAsync("a.cs");
        await SeedAsync("b.cs");
        var hashA = await HashAsync("a.cs", "C.A");
        var hashB = await HashAsync("b.cs", "C.A");
        var writeFailure = new InvalidOperationException("disk exploded");
        var restoreFailure = new IOException("restore refused");
        _fs.WriteOutcomesByPath[Virtual("a.cs")] = new Queue<Exception?>([null, restoreFailure]);
        _fs.WriteOutcomesByPath[Virtual("b.cs")] = new Queue<Exception?>([writeFailure]);
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.A", hashA, NewA));
        tx.Add(Virtual("b.cs"), Replace("C.A", hashB, NewA));

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));

        Assert.Contains(writeFailure, ex.InnerExceptions);
        Assert.Contains(restoreFailure, ex.InnerExceptions);
        Assert.Single(JournalFiles());
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldDeleteJournal_WhenCommitSucceeds()
    {
        await SeedAsync("a.cs");
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.A", await HashAsync("a.cs", "C.A"), NewA));

        await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        Assert.Contains(_fs.Writes, w => w.Path.StartsWith("/output/edit-batch/", StringComparison.Ordinal)
            && w.Path.EndsWith(".journal", StringComparison.Ordinal));
        Assert.Empty(JournalFiles());
        Assert.Equal(TwoMethods.Replace("public int A() { return 1; }", NewA, StringComparison.Ordinal),
            Encoding.UTF8.GetString(await OnDiskAsync("a.cs")));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldReplayLeftoverJournals_WhenNextCommitStartsOnTheMount()
    {
        var (a, b) = await LeaveJournalBehindAsync(new InvalidOperationException("disk exploded"), new IOException("restore refused"));
        Assert.Single(JournalFiles());
        await SeedAsync("c.cs");
        var healthy = new FakeCountingFileSystemService(_disk);
        var tx = Transaction(healthy);
        tx.Add(Virtual("c.cs"), Replace("C.A", await HashAsync("c.cs", "C.A"), NewA));

        await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        Assert.Equal(a, await OnDiskAsync("a.cs"));
        Assert.Equal(b, await OnDiskAsync("b.cs"));
        Assert.Empty(JournalFiles());
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldRefuseConcurrentBatch_WhenSameFileIsHeldByAnotherTransaction()
    {
        await SeedAsync("a.cs");
        var hash = await HashAsync("a.cs", "C.A");
        var gate = new FakeBuildValidator();
        var first = Transaction(_fs, gate);
        first.Add(Virtual("a.cs"), Replace("C.A", hash, NewA));
        var firstCommit = first.CommitAsync(EditValidation.Build([Physical("a.cs")]), TestContext.Current.CancellationToken);
        await gate.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = Transaction(new FakeCountingFileSystemService(_disk));
        second.Add(Virtual("a.cs"), Replace("C.A", hash, "public int A() { return 20; }"));

        await Assert.ThrowsAsync<EditConflictException>(
            () => second.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken));

        gate.Release.SetResult();
        var result = await firstCommit;
        Assert.Equal(BuildOutcome.Green, result.Build!.Outcome);
        Assert.Equal(TwoMethods.Replace("public int A() { return 1; }", NewA, StringComparison.Ordinal),
            Encoding.UTF8.GetString(await OnDiskAsync("a.cs")));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldNotOverwriteLaterEdit_WhenLeftoverJournalReplays()
    {
        var (_, b) = await LeaveJournalBehindAsync(new InvalidOperationException("disk exploded"), new IOException("restore refused"));
        var later = Encoding.UTF8.GetBytes(TwoMethods + "// edited by someone else\n");
        await _disk.WriteAllBytesAsync(Virtual("a.cs"), later, CancellationToken.None);
        await SeedAsync("c.cs");
        var tx = Transaction(new FakeCountingFileSystemService(_disk));
        tx.Add(Virtual("c.cs"), Replace("C.A", await HashAsync("c.cs", "C.A"), NewA));

        await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        Assert.Equal(later, await OnDiskAsync("a.cs"));
        Assert.Equal(b, await OnDiskAsync("b.cs"));
        Assert.Empty(JournalFiles());
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldShiftEarlierRange_WhenLaterOperationLandsBeforeItInTheSameFile()
    {
        await SeedAsync("a.cs");
        const string newB = "public int B() { return 20; }";
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C.B", await HashAsync("a.cs", "C.B"), newB));
        tx.Add(Virtual("a.cs"), new NodeInsertOperation(NodeSelector.Parse("C.A"), InsertPosition.Before, "public int Z() {\n    return 0;\n}"));

        var result = await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        var final = Encoding.UTF8.GetString(await OnDiskAsync("a.cs"));
        var bStart = final.IndexOf(newB, StringComparison.Ordinal);
        var zStart = final.IndexOf("public int Z()", StringComparison.Ordinal);
        var zEnd = final.IndexOf('}', zStart) + 1;
        Assert.True(bStart > zStart, final);
        Assert.Equal(new NodeRange(bStart, bStart + newB.Length, Line(final, bStart), Line(final, bStart)), result.Operations[0].Range);
        Assert.Equal(new NodeRange(zStart, zEnd, Line(final, zStart), Line(final, zEnd - 1)), result.Operations[1].Range);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldKeepEarlierRangeStartAndShiftItsEnd_WhenLaterInsertLandsInsideIt()
    {
        await SeedAsync("a.cs");
        const string newC = "public class C\n{\n    public int A() { return 1; }\n}";
        var tx = Transaction(_fs);
        tx.Add(Virtual("a.cs"), Replace("C", await HashAsync("a.cs", "C"), newC));
        tx.Add(Virtual("a.cs"), new NodeInsertOperation(NodeSelector.Parse("C"), InsertPosition.Inside, "public int Z() { return 0; }"));

        var result = await tx.CommitAsync(EditValidation.Parse, TestContext.Current.CancellationToken);

        var final = Encoding.UTF8.GetString(await OnDiskAsync("a.cs"));
        var cStart = final.IndexOf("public class C", StringComparison.Ordinal);
        var cEnd = final.LastIndexOf('}') + 1;
        Assert.Contains("public int Z() { return 0; }", final, StringComparison.Ordinal);
        Assert.Equal(new NodeRange(cStart, cEnd, Line(final, cStart), Line(final, cEnd - 1)), result.Operations[0].Range);
        var inner = result.Operations[1].Range;
        Assert.True(inner.StartByte >= cStart && inner.EndByte <= cEnd, final);
    }

    private static int Line(string text, int offset) => text.Take(offset).Count(c => c == '\n') + 1;
}
