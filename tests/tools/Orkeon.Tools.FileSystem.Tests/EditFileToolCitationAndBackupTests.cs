using System.Text;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class EditFileToolCitationAndBackupTests
{
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;
    private const string File = "/workspace/a.txt";
    private const string CitationContent =
        "```csharp\n// FQN  : Foo.Bar\n// SHA  : sha256:deadbeef\npublic void Bar() {}\n```";

    private static FakeRightsFileSystemService NewFs(string content)
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        fs.AddFile(File, Encoding.UTF8.GetBytes(content));
        return fs;
    }

    private static Task<ToolCallResponse> EditAsync(EditFileTool tool, string oldString, string newString, bool backup = false) =>
        tool.CallAsync(new ToolCallRequest("edit_file", new Dictionary<string, object?>
        {
            [ParamPath] = File, ["old_string"] = oldString, ["new_string"] = newString, ["create_backup"] = backup,
        }));

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldWriteBackup_WhenCreateBackupRequested()
    {
        var fs = NewFs("alpha");
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditAsync(tool, "alpha", "beta", backup: true);

        Assert.True(response.Success, response.Error ?? "Expected success");
        var backupPath = ((Dictionary<string, object?>)response.Result!)["backup_path"] as string;
        Assert.False(string.IsNullOrEmpty(backupPath));
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(backupPath!, TestContext.Current.CancellationToken));
        Assert.Equal("beta", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRejectEdit_WhenCitationValidatorFails()
    {
        var fs = NewFs("alpha");
        var validator = new StubCitationBlockValidator(
            new ValidationResult { IsValid = false, Violations = ["fabricated SHA", "missing body"] });
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll(), null, validator);

        var response = await EditAsync(tool, "alpha", CitationContent);

        Assert.False(response.Success);
        var dict = (Dictionary<string, object?>)response.Result!;
        Assert.False((bool)dict["success"]!);
        var errors = dict["errors"] as IEnumerable<object?>;
        Assert.NotNull(errors);
        Assert.Equal(2, errors!.Count());
        Assert.True(validator.WasCalled);
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldNotCopyOrDeleteAnyBackup_WhenCreateBackupNotRequested()
    {
        var fs = new FakeTruncatingFailingWriteFileSystemService { FirstWriteFailure = null };
        fs.Inner.AddMount("/workspace", All);
        fs.Inner.AddFile(File, "alpha");
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditAsync(tool, "alpha", "beta");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Empty(fs.CopyCalls);
        Assert.Empty(fs.DeleteCalls);
        Assert.Equal("beta", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldKeepEarlierBackup_WhenLaterEditWithoutBackupRunsAgain()
    {
        var fs = NewFs("alpha");
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());
        var first = await EditAsync(tool, "alpha", "beta", backup: true);
        Assert.True(first.Success, first.Error ?? "Expected success");
        var backupPath = (string)((Dictionary<string, object?>)first.Result!)["backup_path"]!;

        var second = await EditAsync(tool, "beta", "gamma");

        Assert.True(second.Success, second.Error ?? "Expected success");
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(backupPath, TestContext.Current.CancellationToken));
        Assert.Equal("gamma", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldKeepExistingBackupUnchanged_WhenBackupPathAlreadyExists()
    {
        var ct = TestContext.Current.CancellationToken;
        using var probeTool = new EditFileTool(NewFs("alpha"), new StubPathValidator().AllowAll());
        var probe = await EditAsync(probeTool, "alpha", "beta", backup: true);
        Assert.True(probe.Success, probe.Error ?? "Expected success");
        var probePath = (string)((Dictionary<string, object?>)probe.Result!)["backup_path"]!;
        var stampAt = Enumerable.Range(0, probePath.Length - 15).Last(i =>
            probePath.AsSpan(i, 15).ToString().Select((c, k) => k == 8 ? c == '_' : char.IsAsciiDigit(c)).All(ok => ok));
        var stampValue = probePath.Substring(stampAt, 15);

        var fs = NewFs("alpha");
        var sentinels = new List<string>();
        var now = DateTime.UtcNow;
        for (var offset = -2; offset <= 10; offset++)
        {
            var path = probePath.Replace(stampValue,
                now.AddSeconds(offset).ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            fs.AddFile(path, Encoding.UTF8.GetBytes("SENTINEL " + path));
            sentinels.Add(path);
        }
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditAsync(tool, "alpha", "beta", backup: true);

        Assert.True(response.Success, response.Error ?? "Expected success");
        foreach (var path in sentinels)
            Assert.Equal("SENTINEL " + path, await fs.Inner.TryReadAllTextAsync(path, ct));
        var backupPath = (string)((Dictionary<string, object?>)response.Result!)["backup_path"]!;
        Assert.DoesNotContain(backupPath, sentinels);
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(backupPath, ct));
        Assert.Equal("beta", await fs.Inner.TryReadAllTextAsync(File, ct));
    }
}
