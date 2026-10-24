using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon quickstart</c> writes the README's two-minute crew, so the README's first block
/// runs from the dotnet tool alone, without a clone of the repository.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class QuickstartCommandTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "orkeon-quickstart-" + Guid.NewGuid().ToString("N"));

    public QuickstartCommandTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task ShouldWriteTheCrewIntoOrkeonQuickstart_WhenNoFolderIsGiven()
    {
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        var target = Path.Combine(_workspace, "orkeon-quickstart");
        Assert.True(File.Exists(Path.Combine(target, "crew.yaml")));
        Assert.True(File.Exists(Path.Combine(target, "appsettings.json")));
        Assert.True(Directory.Exists(Path.Combine(target, "out")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(target, "out")));
        Assert.Contains("orkeon run crew.yaml --mount ./out:/output:rw", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("orkeon run crew.yaml --mount ./out:/output:ro", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldWriteIntoTheFolder_WhenToIsGiven()
    {
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, ["--to", "demo/crew"], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        var target = Path.Combine(_workspace, "demo", "crew");
        Assert.True(File.Exists(Path.Combine(target, "crew.yaml")));
        Assert.True(File.Exists(Path.Combine(target, "appsettings.json")));
        Assert.True(Directory.Exists(Path.Combine(target, "out")));
        Assert.False(Directory.Exists(Path.Combine(_workspace, "orkeon-quickstart")));
    }

    [Fact]
    public async Task ShouldWriteTheExampleByteForByte_WhenItWritesTheCrew()
    {
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        var example = Path.Combine(RepoRoot(), "examples", "quickstart");
        foreach (var file in new[] { "crew.yaml", "appsettings.json" })
        {
            var expected = await File.ReadAllBytesAsync(Path.Combine(example, file), TestContext.Current.CancellationToken);
            var actual = await File.ReadAllBytesAsync(Path.Combine(_workspace, "orkeon-quickstart", file), TestContext.Current.CancellationToken);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task ShouldRefuseAndLeaveTheFolderAlone_WhenTheFolderIsNotEmpty()
    {
        var target = Path.Combine(_workspace, "orkeon-quickstart");
        Directory.CreateDirectory(target);
        var edited = Path.Combine(target, "crew.yaml");
        await File.WriteAllTextAsync(edited, "# edited", TestContext.Current.CancellationToken);
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [], _workspace);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("is not empty", console.Stderr, StringComparison.Ordinal);
        Assert.Equal("# edited", await File.ReadAllTextAsync(edited, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(target, "appsettings.json")));
    }

    [Fact]
    public async Task ShouldRefuse_WhenTheTargetIsAFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "orkeon-quickstart"), "", TestContext.Current.CancellationToken);
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [], _workspace);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("is a file", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldWrite_WhenTheFolderExistsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "orkeon-quickstart"));
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        Assert.True(File.Exists(Path.Combine(_workspace, "orkeon-quickstart", "crew.yaml")));
    }

    [Theory]
    [InlineData("--to")]
    [InlineData("--to=")]
    public async Task ShouldRefuse_WhenToHasNoValue(string argument)
    {
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, [argument], _workspace);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--to needs a directory", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
    }

    [Fact]
    public async Task ShouldRefuse_WhenAnArgumentIsUnknown()
    {
        using var console = new TestConsole();

        var exit = await QuickstartCommand.DispatchAsync(console, ["--bogus"], _workspace);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unknown argument '--bogus'", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
    }

    /// <summary>The verb is reachable from the top-level dispatch.</summary>
    [Fact]
    public async Task ShouldPrintTheUsage_WhenDispatchedWithHelp()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(console, ["quickstart", "--help"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("orkeon quickstart [--to <dir>]", console.Stdout, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
