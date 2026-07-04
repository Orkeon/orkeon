using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Tests.Console;

namespace Orkeon.Cli.Abstractions.Tests.Commands;

public sealed class CommandContextTests
{
    [Fact]
    public void CommandContext_record_with_required_fields_is_constructible()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        using var console = new TestConsoleAdapter();
        var ctx = new CommandContext
        {
            RawInput = "hello",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = sp
        };

        Assert.Equal("hello", ctx.RawInput);
        Assert.Empty(ctx.Args);
    }

    [Fact]
    public void CommandContext_args_are_never_null()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        using var console = new TestConsoleAdapter();
        var ctx = new CommandContext
        {
            RawInput = "x",
            Args = new[] { "a", "b" },
            Console = console,
            Scope = sp
        };

        Assert.NotNull(ctx.Args);
        Assert.Equal(2, ctx.Args.Count);
    }

    [Fact]
    public void CommandContext_supports_record_with_for_test_overrides()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        using var console = new TestConsoleAdapter();
        var original = new CommandContext
        {
            RawInput = "x",
            Args = Array.Empty<string>(),
            Console = console,
            Scope = sp
        };

        var modified = original with { RawInput = "y" };

        Assert.Equal("x", original.RawInput);
        Assert.Equal("y", modified.RawInput);
    }
}
