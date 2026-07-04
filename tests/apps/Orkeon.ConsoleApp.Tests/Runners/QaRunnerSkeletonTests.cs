using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Runners;

public sealed class QaRunnerSkeletonTests
{
    private static QaRunner Build(IConsoleAdapter console, IFileSystemService fs, ConsoleInputService? input = null)
    {
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var sp = new ServiceCollection().BuildServiceProvider();
        return new QaRunner(
            defaults,
            new QaCommandRegistry(),
            console,
            input ?? new ConsoleInputService(console),
            fs,
            NullLogger<QaRunner>.Instance,
            sp);
    }

    [Fact]
    public void QaRunner_constructor_resolves_dependencies()
    {
        using var console = new TestConsoleAdapter();
        var runner = Build(console, new FakeFileSystemService());
        Assert.NotNull(runner);
    }

    [Fact]
    public void QaRunner_banner_contains_qa_session_header()
    {
        using var console = new TestConsoleAdapter();
        var runner = Build(console, new FakeFileSystemService());
        var bannerProp = typeof(QaRunner).GetProperty("Banner",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var banner = (string?)bannerProp!.GetValue(runner);

        Assert.NotNull(banner);
        Assert.Contains("Interactive Q&A Session", banner!);
    }

    [Fact]
    public void QaRunner_prompt_is_question_arrow()
    {
        using var console = new TestConsoleAdapter();
        var runner = Build(console, new FakeFileSystemService());
        var promptProp = typeof(QaRunner).GetProperty("Prompt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var prompt = (string?)promptProp!.GetValue(runner);

        Assert.Equal("[Question] > ", prompt);
    }

    [Fact]
    public async Task QaRunner_OnStartAsync_with_valid_yaml_path_sets_CrewConfigPath()
    {
        using var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        fs.AddFile("/some/path.yaml", "name: x");
        var input = new ConsoleInputService(console);
        var runner = Build(console, fs, input);

        var sendTask = Task.Run(async () =>
        {
            await console.WaitUntilBlockedAsync();
            await console.SendLineAsync("/some/path.yaml");
        }, TestContext.Current.CancellationToken);

        var startMethod = typeof(QaRunner).GetMethod("OnStartAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var startTask = Task.Run(() => (Task)startMethod.Invoke(runner, new object[] { CancellationToken.None })!, TestContext.Current.CancellationToken);
        await Task.WhenAll(startTask, sendTask);

        Assert.Equal("/some/path.yaml", runner.CrewConfigPath);
    }

    [Fact]
    public async Task QaRunner_OnStartAsync_with_invalid_path_throws_FileNotFound()
    {
        using var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        var input = new ConsoleInputService(console);
        var runner = Build(console, fs, input);

        var sendTask = Task.Run(async () =>
        {
            await console.WaitUntilBlockedAsync();
            await console.SendLineAsync("/missing.yaml");
        }, TestContext.Current.CancellationToken);

        var startMethod = typeof(QaRunner).GetMethod("OnStartAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            var t = (Task)startMethod.Invoke(runner, new object[] { CancellationToken.None })!;
            await Task.WhenAll(t, sendTask);
        });
    }
}
