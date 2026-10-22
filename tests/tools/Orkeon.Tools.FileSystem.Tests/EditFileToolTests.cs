using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class EditFileToolTests : IDisposable
{
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;
    private const string File = "/workspace/a.txt";

    private readonly FakeRightsFileSystemService _fs;
    private readonly EditFileTool _tool;

    public EditFileToolTests()
    {
        _fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        _tool = new EditFileTool(_fs, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private static Task<ToolCallResponse> EditWith(EditFileTool tool, string oldString, string newString, bool replaceAll = false, string path = File) =>
        tool.CallAsync(new ToolCallRequest("edit_file", new Dictionary<string, object?>
        {
            [ParamPath] = path,
            ["old_string"] = oldString,
            ["new_string"] = newString,
            ["replace_all"] = replaceAll,
        }));

    private Task<ToolCallResponse> EditAsync(string oldString, string newString, bool replaceAll = false, string path = File) =>
        EditWith(_tool, oldString, newString, replaceAll, path);

    private static int Replacements(ToolCallResponse r) =>
        Convert.ToInt32(((Dictionary<string, object?>)r.Result!)["replacements"]);

    private async Task<string?> ReadAsync(string path = File) =>
        await _fs.Inner.TryReadAllTextAsync(path, TestContext.Current.CancellationToken);

    private void Seed(string content) => _fs.AddFile(File, Encoding.UTF8.GetBytes(content));

    private static FakeRightsFileSystemService FsWith(FileAccessRights rights)
    {
        var fs = new FakeRightsFileSystemService().AddMount("/workspace", rights);
        fs.AddFile(File, Encoding.UTF8.GetBytes("alpha"));
        return fs;
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReplaceOnce_WhenOldStringIsUnique()
    {
        Seed("alpha beta gamma");

        var response = await EditAsync("beta", "BETA");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(1, Replacements(response));
        Assert.Equal("alpha BETA gamma", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldFailWithCount_WhenOldStringMatchesSeveralTimes()
    {
        Seed("x x x");

        var response = await EditAsync("x", "y");

        Assert.False(response.Success);
        Assert.Contains("3", response.Error);
        Assert.Contains("replace_all", response.Error);
        Assert.Equal("x x x", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldFail_WhenOldStringNotFound()
    {
        Seed("alpha");

        var response = await EditAsync("zeta", "y");

        Assert.False(response.Success);
        Assert.Contains("not found", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("alpha", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldReplaceAll_WhenReplaceAllRequested()
    {
        Seed("x-x-x");

        var response = await EditAsync("x", "yy", replaceAll: true);

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal(3, Replacements(response));
        Assert.Equal("yy-yy-yy", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuseEdit_WhenMountIsReadOnly()
    {
        var fs = FsWith(FileAccessRights.Read);
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditWith(tool, "alpha", "beta");

        Assert.False(response.Success);
        Assert.Contains((File, FileAccessRights.Write), fs.Validations);
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuse_WhenOldStringEmpty()
    {
        Seed("alpha");

        var response = await EditAsync("", "y");

        Assert.False(response.Success);
        Assert.Contains("old_string", response.Error);
        Assert.Equal("alpha", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuse_WhenOldStringEqualsNewString()
    {
        Seed("alpha");

        var response = await EditAsync("alpha", "alpha");

        Assert.False(response.Success);
        Assert.Contains("old_string", response.Error);
        Assert.Contains("new_string", response.Error);
        Assert.Equal("alpha", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldFail_WhenFileMissing()
    {
        var response = await EditAsync("a", "b", path: "/workspace/missing.txt");

        Assert.False(response.Success);
        Assert.Contains("not found", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await ReadAsync("/workspace/missing.txt"));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldCountNonOverlappingOrdinalMatches_WhenOldStringOverlapsItself()
    {
        Seed("aaaa");
        var all = await EditAsync("aa", "b", replaceAll: true);
        Assert.True(all.Success, all.Error ?? "Expected success");
        Assert.Equal(2, Replacements(all));
        Assert.Equal("bb", await ReadAsync());

        Seed("aA aa");
        var single = await EditAsync("aa", "X");
        Assert.True(single.Success, single.Error ?? "Expected success");
        Assert.Equal(1, Replacements(single));
        Assert.Equal("aA X", await ReadAsync());
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuse_WhenReadRightMissing()
    {
        var fs = FsWith(FileAccessRights.Write | FileAccessRights.Create);
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditWith(tool, "alpha", "beta");

        Assert.False(response.Success);
        Assert.Contains((File, FileAccessRights.Read), fs.Validations);
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuse_WhenWriteRightMissing()
    {
        var fs = FsWith(FileAccessRights.Read | FileAccessRights.Create);
        using var tool = new EditFileTool(fs, new StubPathValidator().AllowAll());

        var response = await EditWith(tool, "alpha", "beta");

        Assert.False(response.Success);
        Assert.Contains((File, FileAccessRights.Write), fs.Validations);
        Assert.Equal("alpha", await fs.Inner.TryReadAllTextAsync(File, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldMarkStoreDirty_WhenFileEdited()
    {
        var invalidation = new FakeIndexInvalidation();
        using var tool = new EditFileTool(_fs, new StubPathValidator().AllowAll(), null, null, invalidation);
        Seed("alpha");

        var response = await EditWith(tool, "alpha", "beta");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal([File], invalidation.Marked);
    }
}
