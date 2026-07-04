using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Commands;

namespace Orkeon.Cli.Tests.Commands;

public sealed class ExitCommandTests
{
    private static CommandContext MakeCtx()
    {
        return new CommandContext
        {
            RawInput = "exit",
            Args = Array.Empty<string>(),
            Console = new TestConsoleAdapter(),
            Scope = new ServiceCollection().BuildServiceProvider()
        };
    }

    [Fact]
    public async Task Execute_returns_ShouldExit_true()
    {
        var cmd = new ExitCommand();
        var result = await cmd.ExecuteAsync(MakeCtx(), CancellationToken.None);

        Assert.True(result.ShouldExit);
    }

    [Fact]
    public async Task Execute_returns_farewell_message()
    {
        var cmd = new ExitCommand();
        var result = await cmd.ExecuteAsync(MakeCtx(), CancellationToken.None);

        Assert.Equal("Goodbye!", result.Message);
    }

    [Fact]
    public void Aliases_contain_quit_and_q()
    {
        var cmd = new ExitCommand();

        Assert.Contains("quit", cmd.Aliases);
        Assert.Contains("q", cmd.Aliases);
    }
}
