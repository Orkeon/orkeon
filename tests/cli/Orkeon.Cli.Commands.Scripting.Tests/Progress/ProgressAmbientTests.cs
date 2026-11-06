using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;

namespace Orkeon.Cli.Commands.Scripting.Tests.Progress;

public sealed class ProgressAmbientTests
{
    private static CommandInstance Instance(string ticket)
        => new(ticket, "cmd", CommandInstanceKind.Async, "agent", "intent", Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldSetConsoleThreadToTicket_WhenInstanceIsSet()
    {
        try
        {
            ProgressAmbient.CurrentInstance = Instance("t1");

            Assert.Equal("t1", ConsoleThread.Current);
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
            ConsoleThread.Current = null;
        }
    }

    [Fact]
    [Trait("BR", "A3")]
    public void ShouldResetConsoleThreadToMain_WhenInstanceIsCleared()
    {
        try
        {
            ProgressAmbient.CurrentInstance = Instance("t1");
            Assert.Equal("t1", ConsoleThread.Current);

            ProgressAmbient.CurrentInstance = null;

            Assert.Null(ConsoleThread.Current);
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
            ConsoleThread.Current = null;
        }
    }

    [Theory]
    [InlineData(false, "T")]
    [InlineData(true, null)]
    public async Task The_console_thread_is_the_ticket_unless_the_work_is_on_main(bool onMain, string? expected)
    {
        var instance = new Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceRegistry().Register(
            "c", Orkeon.Cli.Commands.Scripting.Dispatch.CommandInstanceKind.Async, "crew:c", "c", Guid.NewGuid());
        instance.OnMain = onMain;

        var thread = await Task.Run(() =>
        {
            ProgressAmbient.CurrentInstance = instance;
            return ConsoleThread.Current;
        });

        Assert.Equal(expected is null ? null : instance.Ticket, thread);
    }
}
