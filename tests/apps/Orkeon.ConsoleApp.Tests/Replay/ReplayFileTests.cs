using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>The replay file, read through the VFS: its lines, or why it could not be read.</summary>
public sealed class ReplayFileTests
{
    private readonly FakeFileSystemService _fileSystem = new();

    [Fact]
    public async Task ShouldReadTheLines_WhenTheFileExists()
    {
        _fileSystem.AddFile("/workspace/demo.replay", "/help\n/tasks\n");

        var load = await ReplayFile.LoadAsync(_fileSystem, "/workspace/demo.replay", TestContext.Current.CancellationToken);

        Assert.Null(load.Error);
        Assert.Equal(["/help", "/tasks"], load.Lines!.Select(l => l.Text));
        Assert.Equal("/workspace/demo.replay", load.VirtualPath);
    }

    [Fact]
    public async Task ShouldSayNotFound_WhenTheFileIsMissing()
    {
        var load = await ReplayFile.LoadAsync(_fileSystem, "/workspace/missing.replay", TestContext.Current.CancellationToken);

        Assert.Null(load.Lines);
        Assert.Equal("replay file not found: /workspace/missing.replay", load.Error);
    }

    [Fact]
    public async Task ShouldSayNoFile_WhenThePathIsBlank()
    {
        var load = await ReplayFile.LoadAsync(_fileSystem, "  ", TestContext.Current.CancellationToken);

        Assert.Null(load.Lines);
        Assert.Equal("no replay file given", load.Error);
    }

    [Fact]
    public async Task ShouldSayWhy_WhenTheReadFails()
    {
        _fileSystem.ReadError = new IOException("the disk is gone");

        var load = await ReplayFile.LoadAsync(_fileSystem, "/workspace/demo.replay", TestContext.Current.CancellationToken);

        Assert.Null(load.Lines);
        Assert.Equal("cannot read the replay file /workspace/demo.replay: the disk is gone", load.Error);
    }
}
