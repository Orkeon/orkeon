using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class EditBatchToolTests : IDisposable
{
    private const string FileA = "public class A\n{\n    public int X() { return 1; }\n\n    public int Y() { return 2; }\n}\n";
    private const string FileB = "public class B\n{\n    public int P() { return 3; }\n}\n";
    private const string NewX = "public int X() { return 10; }";
    private const string NewY = "public int Y() { return 20; }";
    private const string NewP = "public int P() { return 30; }";
    private const string InsertedQ = "public int Q() { return 4; }";
    private const string EditedA = "public class A\n{\n    public int X() { return 10; }\n\n    public int Y() { return 20; }\n}\n";
    private const string EditedB = "public class B\n{\n    public int P() { return 30; }\n    public int Q() { return 4; }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("editbatch").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _fs;
    private readonly FakeBuildValidator _build = new();

    public EditBatchToolTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/");
        _fs = new FakeCountingFileSystemService(_disk);
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private string Physical(string name) => Path.Combine(_dir, "workspace", name);

    private async Task SeedAsync()
    {
        await _disk.WriteAllTextAsync("/workspace/A.cs", FileA, CancellationToken.None);
        await _disk.WriteAllTextAsync("/workspace/B.cs", FileB, CancellationToken.None);
    }

    private async Task<string> HashAsync(string file, string selector)
    {
        using var read = new NodeReadTool(_disk, _pool);
        var resp = await read.ExecuteTypedForTest(
            new NodeReadRequest { Path = "/workspace/" + file, Selector = selector }, CancellationToken.None);
        return resp.Sha256;
    }

    private async Task<EditBatchOperation> ReplaceAsync(string file, string selector, string text) => new()
    {
        Kind = "node_replace",
        Path = "/workspace/" + file,
        Selector = selector,
        ExpectedHash = await HashAsync(file, selector),
        Text = text,
    };

    private static EditBatchOperation InsertAfter(string file, string selector, string text) => new()
    {
        Kind = "node_insert",
        Path = "/workspace/" + file,
        Selector = selector,
        Position = "after",
        Text = text,
    };

    private async Task<EditBatchOperation[]> FourOperationsAsync() =>
    [
        await ReplaceAsync("A.cs", "A.X", NewX),
        await ReplaceAsync("A.cs", "A.Y", NewY),
        await ReplaceAsync("B.cs", "B.P", NewP),
        InsertAfter("B.cs", "B.P", InsertedQ),
    ];

    private async Task<EditBatchResponse> RunAsync(EditBatchOperation[] operations, string validate = "parse")
    {
        using var tool = new EditBatchTool(_fs, _pool, _build);
        return await tool.ExecuteTypedForTest<EditBatchRequest, EditBatchResponse>(
            new EditBatchRequest { Operations = operations, Validate = validate }, TestContext.Current.CancellationToken);
    }

    private async Task AssertUntouchedAsync()
    {
        Assert.Equal(FileA, await File.ReadAllTextAsync(Physical("A.cs"), TestContext.Current.CancellationToken));
        Assert.Equal(FileB, await File.ReadAllTextAsync(Physical("B.cs"), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldApplyFourOperationsOverTwoFiles_WhenAllValidate()
    {
        await SeedAsync();

        var response = await RunAsync(await FourOperationsAsync());

        Assert.Equal(EditedA, await File.ReadAllTextAsync(Physical("A.cs"), TestContext.Current.CancellationToken));
        Assert.Equal(EditedB, await File.ReadAllTextAsync(Physical("B.cs"), TestContext.Current.CancellationToken));
        Assert.Equal(4, response.Operations.Count);
        Assert.Equal("parse ok", response.Validation);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldLeaveAllFilesByteIdentical_WhenOneOperationFails()
    {
        await SeedAsync();
        EditBatchOperation[] operations =
        [
            await ReplaceAsync("A.cs", "A.X", NewX),
            await ReplaceAsync("B.cs", "B.P", "public int P() { return 3;"),
        ];

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(operations));

        Assert.StartsWith("The replacement introduces parse errors", ex.Message, StringComparison.Ordinal);
        await AssertUntouchedAsync();
        Assert.DoesNotContain(_fs.Writes, w => w.Path is "/workspace/A.cs" or "/workspace/B.cs");
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldListEachOperationRange_WhenApplied()
    {
        await SeedAsync();
        var startX = FileA.IndexOf("public int X()", StringComparison.Ordinal);
        var startY = FileA.IndexOf("public int Y()", StringComparison.Ordinal) + NewX.Length - "public int X() { return 1; }".Length;

        var response = await RunAsync([await ReplaceAsync("A.cs", "A.X", NewX), await ReplaceAsync("A.cs", "A.Y", NewY)]);

        Assert.Equal("/workspace/A.cs", response.Operations[0].Path);
        Assert.Equal(startX, response.Operations[0].Range.StartByte);
        Assert.Equal(startX + NewX.Length, response.Operations[0].Range.EndByte);
        Assert.Equal(3, response.Operations[0].Range.StartLine);
        Assert.Equal(3, response.Operations[0].Range.EndLine);
        Assert.Equal("/workspace/A.cs", response.Operations[1].Path);
        Assert.Equal(startY, response.Operations[1].Range.StartByte);
        Assert.Equal(startY + NewY.Length, response.Operations[1].Range.EndByte);
        Assert.Equal(5, response.Operations[1].Range.StartLine);
        Assert.Equal(5, response.Operations[1].Range.EndLine);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldRefuseNodeReplace_WhenExpectedHashMissing()
    {
        await SeedAsync();
        var missing = new EditBatchOperation { Kind = "node_replace", Path = "/workspace/A.cs", Selector = "A.X", Text = NewX };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => RunAsync([missing]));

        Assert.Contains("expected_hash", ex.Message, StringComparison.Ordinal);
        await AssertUntouchedAsync();
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldCommit_WhenBuildValidatorReportsGreen()
    {
        await SeedAsync();
        _build.Result = new BuildValidation { Outcome = BuildOutcome.Green };

        var response = await RunAsync(await FourOperationsAsync(), "build");

        Assert.Equal("build green", response.Validation);
        Assert.Single(_build.Calls);
        Assert.Equal(EditedA, await File.ReadAllTextAsync(Physical("A.cs"), TestContext.Current.CancellationToken));
        Assert.Equal(EditedB, await File.ReadAllTextAsync(Physical("B.cs"), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldRestoreOriginalBytes_WhenBuildValidatorReportsRed()
    {
        await SeedAsync();
        _build.Result = new BuildValidation { Outcome = BuildOutcome.Red, Detail = "error CS0103" };

        var response = await RunAsync(await FourOperationsAsync(), "build");

        Assert.StartsWith("build red", response.Validation, StringComparison.Ordinal);
        await AssertUntouchedAsync();
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldKeepFilesAndReportSkipped_WhenBuildValidatorReportsSkipped()
    {
        await SeedAsync();
        _build.Result = new BuildValidation { Outcome = BuildOutcome.Skipped };

        var response = await RunAsync(await FourOperationsAsync(), "build");

        Assert.Equal("build check skipped", response.Validation);
        Assert.Equal(EditedA, await File.ReadAllTextAsync(Physical("A.cs"), TestContext.Current.CancellationToken));
        Assert.Equal(EditedB, await File.ReadAllTextAsync(Physical("B.cs"), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldHandPhysicalResolvedPathsToValidator_WhenValidateIsBuild()
    {
        await SeedAsync();

        await RunAsync(await FourOperationsAsync(), "build");

        var call = Assert.Single(_build.Calls);
        Assert.Equal([Physical("A.cs"), Physical("B.cs")], call);
    }
}
