using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;

namespace Orkeon.Cli.Tests.Registry;

public sealed class DefaultCommandRegistryTests
{
    private static readonly string[] ExpectedDefaultCommandNames = ["help", "exit", "clear"];

    [Fact]
    public void Commands_returns_three_in_order_help_exit_clear()
    {
        var reg = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());

        var names = reg.Commands.Select(c => c.Name).ToArray();

        Assert.Equal(ExpectedDefaultCommandNames, names);
    }

    [Fact]
    public void Fallback_is_null()
    {
        var reg = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());

        Assert.Null(reg.Fallback);
    }

    [Fact]
    public void Commands_enumeration_is_stable_across_multiple_calls()
    {
        var reg = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());

        var first = reg.Commands.ToArray();
        var second = reg.Commands.ToArray();

        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++)
            Assert.Same(first[i], second[i]);
    }
}
