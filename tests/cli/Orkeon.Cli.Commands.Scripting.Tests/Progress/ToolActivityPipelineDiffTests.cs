using Orkeon.Application.Interfaces.Security;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Progress;

public sealed class ToolActivityPipelineDiffTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tool-diff-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_(string name) => Path.Combine(_dir, name);

    private static Task<ToolCallFinished> Run(string tool, string path, Action write, bool success = true)
        => Run(new PassThroughFileSystemService(), tool, path, write, success);

    private static async Task<ToolCallFinished> Run(IFileSystemService files, string tool, string path, Action write, bool success = true)
    {
        var observer = new FakeToolActivityObserver();
        var inner = new FakeToolInvocationPipeline { OnInvoke = write, Success = success };
        var pipeline = new ToolActivityPipeline(inner, new ToolActivityFeed { Observer = observer }, files);
        var arguments = new Dictionary<string, object?> { ["path"] = path };
        await pipeline.InvokeAsync(new ToolInvocation(new FakeNamedTool(tool), arguments, new ToolInvocationCaller("a", "a")), TestContext.Current.CancellationToken);
        return observer.LastFinished!;
    }

    [Fact]
    public async Task An_edit_reports_what_changed_in_the_file()
    {
        // Arrange
        var path = File_("a.txt");
        await File.WriteAllTextAsync(path, "one\ntwo\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, "one\nTWO\n"));

        // Assert
        Assert.Equal((1, 1), (finished.Diff!.Added, finished.Diff.Removed));
    }

    [Fact]
    public async Task A_new_file_is_all_added()
    {
        // Arrange
        var path = File_("new.txt");

        // Act
        var finished = await Run("file_write", path, () => File.WriteAllText(path, "one\ntwo\n"));

        // Assert
        Assert.Equal((2, 0), (finished.Diff!.Added, finished.Diff.Removed));
    }

    [Fact]
    public async Task A_call_that_changes_nothing_reports_no_change()
    {
        // Arrange
        var path = File_("same.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, "one\n"));

        // Assert
        Assert.Null(finished.Diff);
    }

    [Fact]
    public async Task A_failed_call_reports_no_change()
    {
        // Arrange
        var path = File_("failed.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, "two\n"), success: false);

        // Assert
        Assert.Null(finished.Diff);
    }

    [Fact]
    public async Task Another_tool_reports_no_change()
    {
        // Arrange
        var path = File_("other.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("file_read", path, () => File.WriteAllText(path, "two\n"));

        // Assert
        Assert.Null(finished.Diff);
    }

    [Fact]
    public async Task A_binary_file_reports_no_change()
    {
        // Arrange
        var path = File_("bin.dat");
        await File.WriteAllBytesAsync(path, [1, 0, 2], TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("file_write", path, () => File.WriteAllText(path, "text\n"));

        // Assert
        Assert.Null(finished.Diff);
    }

    [Fact]
    public async Task A_file_too_large_reports_no_change()
    {
        // Arrange
        var path = File_("large.txt");
        await File.WriteAllTextAsync(path, new string('x', (1024 * 1024) + 1), TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("file_write", path, () => File.WriteAllText(path, "text\n"));

        // Assert
        Assert.Null(finished.Diff);
    }

    [Fact]
    public async Task A_relative_path_is_read_under_the_root_of_the_agent_run()
    {
        // Arrange
        await using var run = AgentRunScope.Begin("a", "a", _dir);
        var path = File_("a.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", "a.txt", () => File.WriteAllText(path, "two\n"));

        // Assert
        Assert.Equal((1, 1), (finished.Diff!.Added, finished.Diff.Removed));
    }

    [Fact]
    public async Task A_path_with_a_run_of_spaces_is_read_as_the_tool_reads_it()
    {
        // Arrange
        var path = File_("a  b.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, "two\n"));

        // Assert
        Assert.Equal((1, 1), (finished.Diff!.Added, finished.Diff.Removed));
    }

    [Fact]
    public async Task A_path_longer_than_the_subject_is_read_whole()
    {
        // Arrange
        var directory = Directory.CreateDirectory(Path.Combine(_dir, new string('d', 200), new string('e', 200))).FullName;
        var path = Path.Combine(directory, "a.txt");
        await File.WriteAllTextAsync(path, "one\n", TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, "two\n"));

        // Assert
        Assert.Equal((1, 1), (finished.Diff!.Added, finished.Diff.Removed));
    }

    [Fact]
    public async Task A_file_too_large_is_not_loaded()
    {
        // Arrange
        var path = File_("large.txt");
        await File.WriteAllTextAsync(path, new string('x', (1024 * 1024) + 1), TestContext.Current.CancellationToken);
        var files = new CountingDiscoveryFileSystemService(new PassThroughFileSystemService());

        // Act
        await Run(files, "file_write", path, () => { });

        // Assert
        Assert.Equal(0, files.ReadBytesCalls);
    }

    [Fact]
    public async Task An_entry_of_no_length_is_not_opened()
    {
        // Arrange
        var path = File_("empty.txt");
        await File.WriteAllTextAsync(path, "", TestContext.Current.CancellationToken);
        var files = new CountingDiscoveryFileSystemService(new PassThroughFileSystemService());

        // Act
        await Run(files, "file_write", path, () => { });

        // Assert
        Assert.Equal(0, files.ReadBytesCalls);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    [InlineData("a\nb\n", "a\nb")]
    public async Task A_change_of_line_endings_alone_shows_no_diff(string before, string after)
    {
        // Arrange
        var path = File_("a.txt");
        await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);

        // Act
        var finished = await Run("edit_file", path, () => File.WriteAllText(path, after));

        // Assert
        Assert.Null(finished.Diff);
    }
}
