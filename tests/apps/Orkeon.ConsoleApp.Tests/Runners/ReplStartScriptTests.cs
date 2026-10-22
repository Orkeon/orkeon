using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Abstractions.Tests.Fixtures;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Constants.Cli;
using Orkeon.Infrastructure.Session;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Runners;

public sealed class ReplStartScriptTests
{
    private const string ScriptPath = "/workspace/start.sh";
    private const string Stale = "[Session start script: stale]\nold output";

    private static ReplStartScriptHook NewHook(FakeStartScriptRunner fake, ISessionBufferService session, string? script = ScriptPath)
        => new(
            fake,
            session,
            Options.Create(new CliSessionOptions { StartScript = script }),
            NullLogger<ReplStartScriptHook>.Instance);

    private static async Task<List<string>> RunReplWithOneTurnAsync(
        ISessionBufferService session, string line, bool cancelBeforeExit = false)
    {
        using var console = new TestConsoleAdapter();
        var assistant = new RecordingCommand("assistant");
        var services = new ServiceCollection().AddSingleton(session).BuildServiceProvider();
        var runner = new ScriptedCommandsRunner(
            new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand()),
            new ScriptCommandRegistry(Array.Empty<ScriptCommand>(), [assistant]),
            console,
            NullLogger<ScriptedCommandsRunner>.Instance,
            services,
            Options.Create(new ScriptCommandsConfiguration()));

        using var cts = new CancellationTokenSource();
        var run = Task.Run(() => runner.RunAsync(cts.Token));
        await console.WaitUntilBlockedAsync();
        await console.SendLineAsync(line);
        await console.WaitUntilBlockedAsync();
        if (cancelBeforeExit)
            await cts.CancelAsync();
        console.Complete();
        await run;
        return assistant.Invocations.Select(c => c.RawInput).ToList();
    }

    private static async Task AssertNoStalePrefixAsync(FakeStartScriptRunner fake, string? script)
    {
        var session = new InMemorySessionBufferService();
        session.SetState(SessionStateKeys.SessionStartOutput, Stale);

        await NewHook(fake, session, script).RunOnceAsync(CancellationToken.None);
        var inputs = await RunReplWithOneTurnAsync(session, "fix the bug");

        Assert.Equal(["fix the bug"], inputs);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldRunStartScriptOnce_WhenReplStartsWithScriptConfigured()
    {
        var fake = new FakeStartScriptRunner { Output = new StartScriptOutput("hello", "echo hello") };
        var hook = NewHook(fake, new InMemorySessionBufferService());

        await hook.RunOnceAsync(CancellationToken.None);
        await hook.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(ScriptPath, fake.LastPath);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldInjectScriptOutputAndSourceIntoFirstTurn_WhenScriptProducesOutput()
    {
        var fake = new FakeStartScriptRunner { Output = new StartScriptOutput("branch=main", "git branch --show-current") };
        var session = new InMemorySessionBufferService();
        await NewHook(fake, session).RunOnceAsync(CancellationToken.None);

        var inputs = await RunReplWithOneTurnAsync(session, "fix the bug");

        var first = Assert.Single(inputs);
        var outputAt = first.IndexOf("branch=main", StringComparison.Ordinal);
        Assert.True(outputAt >= 0, first);
        Assert.Contains("git branch --show-current", first);
        Assert.Contains(ScriptPath, first);
        Assert.True(outputAt < first.IndexOf("fix the bug", StringComparison.Ordinal), first);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldStartRepl_WhenStartScriptFails()
    {
        var fake = new FakeStartScriptRunner { ToThrow = new InvalidOperationException("boom") };
        var session = new InMemorySessionBufferService();

        await NewHook(fake, session).RunOnceAsync(CancellationToken.None);
        var inputs = await RunReplWithOneTurnAsync(session, "fix the bug");

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(["fix the bug"], inputs);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public Task ShouldNotPrependStaleStartOutput_WhenScriptFails()
        => AssertNoStalePrefixAsync(new FakeStartScriptRunner { ToThrow = new InvalidOperationException("boom") }, ScriptPath);

    [Fact]
    [Trait("BR", "RT-05")]
    public Task ShouldNotPrependStaleStartOutput_WhenScriptPrintsNothing()
        => AssertNoStalePrefixAsync(new FakeStartScriptRunner(), ScriptPath);

    [Fact]
    [Trait("BR", "RT-05")]
    public Task ShouldNotPrependStaleStartOutput_WhenNoScriptConfigured()
        => AssertNoStalePrefixAsync(new FakeStartScriptRunner(), null);

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldSaveSession_WhenReplTokenIsCancelledBeforeExit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orkeon-f15-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fs = new DiskBackedFileSystemService(dir, "/workspace");
            var session = new FileSessionBufferService(fs, new CliSessionOptions { SessionsDirectory = "/workspace/sessions" });
            await session.LoadAsync("/workspace/project", TestContext.Current.CancellationToken);
            session.SetState("marker", "kept");

            await RunReplWithOneTurnAsync(session, "fix the bug", cancelBeforeExit: true);

            var saved = Directory.Exists(dir) ? Directory.GetFiles(dir, "last.json", SearchOption.AllDirectories) : [];
            var file = Assert.Single(saved);
            Assert.Contains("kept", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
