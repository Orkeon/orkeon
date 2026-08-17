using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Domain.Common;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

public sealed class CommandInstanceRegistryTests
{
    [Fact]
    public void Register_starts_running_with_unique_ticket()
    {
        var registry = new CommandInstanceRegistry();
        var a = registry.Register("ask", CommandInstanceKind.Sync, "echo", "run", Guid.NewGuid());
        var b = registry.Register("ask", CommandInstanceKind.Sync, "echo", "run", Guid.NewGuid());

        Assert.NotEqual(a.Ticket, b.Ticket);
        Assert.Equal(CommandInstanceState.Running, a.State);
        Assert.False(a.IsTerminal);
    }

    [Fact]
    public void Complete_marks_done_and_freezes_elapsed()
    {
        var registry = new CommandInstanceRegistry();
        var instance = registry.Register("ask", CommandInstanceKind.Async, "echo", "run", Guid.NewGuid());

        instance.Complete(new CommandResponse("echo", "run", success: true, payload: "PONG", error: null));

        var view = registry.Get(instance.Ticket)!.Snapshot();
        Assert.Equal("done", view.state);
        Assert.Equal("PONG", view.result!.payload);
        Assert.NotNull(view.completedAt);
    }

    [Fact]
    public void Terminal_transition_is_one_shot()
    {
        var instance = new CommandInstanceRegistry()
            .Register("ask", CommandInstanceKind.Sync, "echo", "run", Guid.NewGuid());

        instance.Complete(new CommandResponse("echo", "run", true, "first", null));
        instance.Fail("ignored — already terminal");

        Assert.Equal(CommandInstanceState.Done, instance.State);
    }

    [Fact]
    public void AttachTerminalCallback_fires_immediately_when_already_terminal()
    {
        var instance = new CommandInstanceRegistry()
            .Register("ask", CommandInstanceKind.Async, "echo", "run", Guid.NewGuid());
        instance.Complete(new CommandResponse("echo", "run", true, "x", null));

        var fired = 0;
        instance.AttachTerminalCallback(_ => Interlocked.Increment(ref fired));

        Assert.Equal(1, fired);
    }

    [Fact]
    public void AttachTerminalCallback_fires_once_on_later_completion()
    {
        var instance = new CommandInstanceRegistry()
            .Register("ask", CommandInstanceKind.Async, "echo", "run", Guid.NewGuid());

        var fired = 0;
        instance.AttachTerminalCallback(_ => Interlocked.Increment(ref fired));
        instance.Complete(new CommandResponse("echo", "run", true, "x", null));
        instance.Cancel(); // already terminal — must not refire

        Assert.Equal(1, fired);
    }

    [Fact]
    public void List_filters_by_state_name_and_agent()
    {
        var registry = new CommandInstanceRegistry();
        var done = registry.Register("ask", CommandInstanceKind.Sync, "echo", "run", Guid.NewGuid());
        done.Complete(new CommandResponse("echo", "run", true, "ok", null));
        registry.Register("askbg", CommandInstanceKind.Async, "writer", "run", Guid.NewGuid()); // running

        Assert.Single(registry.List(new CommandInstanceFilter(State: CommandInstanceState.Done)));
        Assert.Single(registry.List(new CommandInstanceFilter(State: CommandInstanceState.Running)));
        Assert.Single(registry.List(new CommandInstanceFilter(Agent: "echo")));
        Assert.Single(registry.List(new CommandInstanceFilter(Name: "askbg")));
        Assert.Equal(2, registry.List().Count);
    }

    [Fact]
    public void Retention_evicts_oldest_terminal_entries_over_the_limit()
    {
        var registry = new CommandInstanceRegistry { RetentionLimit = 3 };
        for (var i = 0; i < 10; i++)
        {
            var inst = registry.Register("ask", CommandInstanceKind.Sync, "echo", "run", Guid.NewGuid());
            inst.Complete(new CommandResponse("echo", "run", true, "ok", null));
        }

        Assert.True(registry.Count <= 3, $"expected ≤ 3 retained, got {registry.Count}");
    }

    [Fact]
    public void Directory_registers_resolves_and_unregisters()
    {
        var directory = new AgentCommandDirectory();
        var id = AgentId.Create();

        var handle = directory.Register("echo", id);
        Assert.Equal(id, directory.Resolve("echo"));
        Assert.True(directory.Contains("ECHO")); // case-insensitive

        handle.Dispose();
        Assert.Null(directory.Resolve("echo"));
    }
}
