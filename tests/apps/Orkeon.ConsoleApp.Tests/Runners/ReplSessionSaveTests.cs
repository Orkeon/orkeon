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
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Infrastructure.Session;

namespace Orkeon.ConsoleApp.Tests.Runners;

public sealed class ReplSessionSaveTests
{
    private static string LastJson(FakeFileSystemService fs)
    {
        var entry = fs.Files.Where(f => f.Key.EndsWith("/last.json", StringComparison.Ordinal)).ToList();
        return entry.Count == 1 ? entry[0].Value : string.Empty;
    }

    private static async Task<(TestConsoleAdapter Console, Task Run, CancellationTokenSource Cts)> StartAsync(
        FakeFileSystemService fs)
    {
        var session = new FileSessionBufferService(fs, new CliSessionOptions { SessionsDirectory = "/workspace/sessions" });
        await session.LoadAsync("/workspace/project", TestContext.Current.CancellationToken);
        var console = new TestConsoleAdapter();
        var assistant = new RecordingCommand("assistant", action: ctx => session.AppendNote("note for " + ctx.RawInput));
        var services = new ServiceCollection().AddSingleton<ISessionBufferService>(session).BuildServiceProvider();
        var runner = new ScriptedCommandsRunner(
            new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand()),
            new ScriptCommandRegistry(Array.Empty<ScriptCommand>(), [assistant]),
            console,
            NullLogger<ScriptedCommandsRunner>.Instance,
            services,
            Options.Create(new ScriptCommandsConfiguration()));
        var cts = new CancellationTokenSource();
        var run = Task.Run(() => runner.RunAsync(cts.Token));
        await console.WaitUntilBlockedAsync();
        return (console, run, cts);
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldLeaveTurnInLastJson_WhenReplIsKilledAfterTurnWithoutExit()
    {
        var fs = new FakeFileSystemService();
        var (console, run, cts) = await StartAsync(fs);
        try
        {
            await console.SendLineAsync("fix the bug");
            await console.WaitUntilBlockedAsync();

            Assert.Contains("note for fix the bug", LastJson(fs));
        }
        finally
        {
            console.Complete();
            await run;
            cts.Dispose();
            console.Dispose();
        }
    }

    [Fact]
    [Trait("BR", "RT-14")]
    public async Task ShouldRewriteLastJson_AfterEachTurn()
    {
        var fs = new FakeFileSystemService();
        var (console, run, cts) = await StartAsync(fs);
        try
        {
            await console.SendLineAsync("first turn");
            await console.WaitUntilBlockedAsync();
            var afterFirst = LastJson(fs);

            await console.SendLineAsync("second turn");
            await console.WaitUntilBlockedAsync();
            var afterSecond = LastJson(fs);

            Assert.Contains("note for first turn", afterFirst);
            Assert.DoesNotContain("note for second turn", afterFirst);
            Assert.Contains("note for first turn", afterSecond);
            Assert.Contains("note for second turn", afterSecond);
        }
        finally
        {
            console.Complete();
            await run;
            cts.Dispose();
            console.Dispose();
        }
    }
}
