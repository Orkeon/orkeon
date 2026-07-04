using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Abstractions.Tests.Commands;

public sealed class CommandResultTests
{
    [Fact]
    public void Continue_no_message_sets_shouldExit_false()
    {
        var result = CommandResult.Continue();

        Assert.False(result.ShouldExit);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Continue_with_message_sets_message_and_shouldExit_false()
    {
        var result = CommandResult.Continue("hello");

        Assert.False(result.ShouldExit);
        Assert.Equal("hello", result.Message);
    }

    [Fact]
    public void Exit_no_farewell_sets_shouldExit_true()
    {
        var result = CommandResult.Exit();

        Assert.True(result.ShouldExit);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Exit_with_farewell_sets_message()
    {
        var result = CommandResult.Exit("bye");

        Assert.True(result.ShouldExit);
        Assert.Equal("bye", result.Message);
    }

    [Fact]
    public void CommandResult_records_with_same_values_are_equal()
    {
        var a = CommandResult.Continue("x");
        var b = CommandResult.Continue("x");

        Assert.Equal(a, b);
    }

    [Fact]
    public void CommandResult_with_modifies_specific_field()
    {
        var a = CommandResult.Continue("x");
        var b = a with { Message = "y" };

        Assert.Equal("x", a.Message);
        Assert.Equal("y", b.Message);
        Assert.Equal(a.ShouldExit, b.ShouldExit);
    }
}
