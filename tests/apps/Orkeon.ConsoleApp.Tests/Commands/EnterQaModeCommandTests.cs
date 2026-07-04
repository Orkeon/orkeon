using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.ConsoleApp.Commands;
using Orkeon.ConsoleApp.Commands.Qa;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Commands;

public sealed class EnterQaModeCommandTests
{
    private static (CommandContext ctx, TestConsoleAdapter console, QaRunner runner) Build()
    {
        var console = new TestConsoleAdapter();
        var fs = new FakeFileSystemService();
        fs.AddFile("/tmp/test.yaml", "name: x");

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IConsoleAdapter>(console);
        services.AddSingleton(new ConsoleInputService(console));
        services.AddSingleton<IFileSystemService>(fs);
        services.AddSingleton<ICrewFactory>(new FakeCrewFactory());
        services.AddSingleton<ICrewOrchestrationService>(new FakeCrewOrchestrationService());
        services.AddOrkeonCli();
        services.AddSingleton<AskQuestionCommand>();
        services.AddSingleton<QaCommandRegistry>(sp =>
            new QaCommandRegistry(sp.GetRequiredService<AskQuestionCommand>()));
        services.AddSingleton<QaRunner>();
        var sp2 = services.BuildServiceProvider();

        var runner = sp2.GetRequiredService<QaRunner>();
        var ctx = new CommandContext
        {
            RawInput = "qa",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = sp2
        };
        return (ctx, console, runner);
    }

    [Fact]
    public void Name_is_qa()
        => Assert.Equal("qa", new EnterQaModeCommand().Name);

    [Fact]
    public void Implements_IMainMenuCommand()
        => Assert.True(typeof(IMainMenuCommand).IsAssignableFrom(typeof(EnterQaModeCommand)));

    [Fact]
    public async Task Execute_resolves_QaRunner_and_runs_to_exit()
    {
        var (ctx, console, _) = Build();
        var cmd = new EnterQaModeCommand();

        // Drive the QA runner: send YAML path then exit
        var sendTask = Task.Run(async () =>
        {
            await console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            await console.SendLineAsync("/tmp/test.yaml");
            await console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            await console.SendLineAsync("exit");
        }, TestContext.Current.CancellationToken);

        var execTask = Task.Run(() => cmd.ExecuteAsync(ctx, CancellationToken.None));
        await Task.WhenAll(execTask, sendTask);

        var result = await execTask;
        Assert.False(result.ShouldExit);
        Assert.Equal("Returned from Q&A mode.", result.Message);
    }
}
