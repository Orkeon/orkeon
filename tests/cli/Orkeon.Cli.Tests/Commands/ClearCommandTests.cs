using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Commands;

namespace Orkeon.Cli.Tests.Commands;

public sealed class ClearCommandTests
{
    [Fact]
    public async Task Execute_calls_Console_Clear()
    {
        var console = new TestConsoleAdapter();
        var ctx = new CommandContext
        {
            RawInput = "clear",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = new ServiceCollection().BuildServiceProvider()
        };

        await new ClearCommand().ExecuteAsync(ctx, CancellationToken.None);

        Assert.Equal(1, console.ClearCount);
    }

    [Fact]
    public async Task Execute_returns_Continue()
    {
        var console = new TestConsoleAdapter();
        var ctx = new CommandContext
        {
            RawInput = "clear",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = new ServiceCollection().BuildServiceProvider()
        };

        var result = await new ClearCommand().ExecuteAsync(ctx, CancellationToken.None);

        Assert.False(result.ShouldExit);
    }

    [Fact]
    public void Alias_contains_cls()
    {
        var cmd = new ClearCommand();
        Assert.Contains("cls", cmd.Aliases);
    }
}
