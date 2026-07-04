using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.ConsoleApp.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Tests.Commands;

public sealed class DemoCommandTests
{
    private static (CommandContext ctx, TestConsoleAdapter console) Build()
    {
        var console = new TestConsoleAdapter();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddSingleton<IConsoleAdapter>(console);
        services.AddSingleton(new ConsoleInputService(console));
        services.AddSingleton<AgentManagementService>();
        services.AddSingleton<CrewManagementService>();
        services.AddSingleton<TaskManagementService>();
        var sp = services.BuildServiceProvider();
        var ctx = new CommandContext
        {
            RawInput = "demo",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = sp
        };
        return (ctx, console);
    }

    [Fact]
    public async Task Execute_with_Escape_cancels_with_message()
    {
        var (ctx, console) = Build();
        // Send ESC key when ReadKey is invoked
        var sendTask = Task.Run(async () =>
        {
            await console.WaitUntilBlockedAsync(TimeSpan.FromSeconds(5));
            await console.SendKeyAsync(ConsoleKey.Escape);
        }, TestContext.Current.CancellationToken);

        var execTask = Task.Run(() => new DemoCommand().ExecuteAsync(ctx, CancellationToken.None));
        await Task.WhenAll(execTask, sendTask);

        var result = await execTask;
        Assert.False(result.ShouldExit);
        Assert.Equal("Demo cancelled.", result.Message);
    }

    [Fact]
    public void Name_is_demo()
        => Assert.Equal("demo", new DemoCommand().Name);

    [Fact]
    public void Implements_IMainMenuCommand()
        => Assert.True(typeof(IMainMenuCommand).IsAssignableFrom(typeof(DemoCommand)));
}
