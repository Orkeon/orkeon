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
}
