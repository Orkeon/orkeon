using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Dispatch.Commands;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// The usage errors of the dispatch built-ins and of <c>help-cmd</c> are failed results, so a replay stops on
/// them; a ticket's own failure, which <c>result</c> reports, is not one of the command.
/// </summary>
public sealed class DispatchBuiltinCommandsTests
{
    private readonly ScriptedTestConsole _console = new();
    private readonly CommandInstanceRegistry _registry = new();

    private Task<CommandResult> Run(IInteractiveCommand command, params string[] args)
        => command.ExecuteAsync(
            new CommandContext
            {
                RawInput = command.Name + " " + string.Join(' ', args),
                Args = args,
                Console = _console,
                Scope = ChainHarness.EmptyProvider.Instance,
            },
            TestContext.Current.CancellationToken);

    private CancelCommand Cancel() => new(new CommandDispatchService(
        new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
        new AgentCommandDirectory(),
        _registry));

    [Fact]
    public async Task ShouldFailWithTheUsage_WhenNoTicketIsGiven()
    {
        Assert.Equal("Usage: inspect --ticket=<ticket>", Failed(await Run(new InspectCommand(_registry))));
        Assert.Equal("Usage: result --ticket=<ticket>", Failed(await Run(new ResultCommand(_registry))));
        Assert.Equal("Usage: cancel --ticket=<ticket>", Failed(await Run(Cancel())));
    }

    [Fact]
    public async Task ShouldFail_WhenTheTicketIsUnknown()
    {
        Assert.Equal("Unknown ticket 't9'.", Failed(await Run(new InspectCommand(_registry), "--ticket=t9")));
        Assert.Equal("Unknown ticket 't9'.", Failed(await Run(new ResultCommand(_registry), "--ticket=t9")));
        Assert.Equal("[t9] not cancellable (unknown or already terminal).", Failed(await Run(Cancel(), "--ticket=t9")));
    }

    [Fact]
    public async Task ShouldNotFail_WhenCancelNamesATicketAlreadyTerminal()
    {
        var instance = _registry.Register("ask", CommandInstanceKind.Async, "echo", "run", Guid.NewGuid());
        instance.Fail("broken");

        var result = await Run(Cancel(), "--ticket=" + instance.Ticket);

        Assert.False(result.Failed);
        Assert.Equal($"[{instance.Ticket}] not cancellable (unknown or already terminal).", result.Message);
    }

    [Fact]
    public async Task ShouldNotFail_WhenResultReportsAFailedTicket()
    {
        var instance = _registry.Register("ask", CommandInstanceKind.Async, "echo", "run", Guid.NewGuid());
        instance.Fail("broken");

        var result = await Run(new ResultCommand(_registry), $"--ticket={instance.Ticket}");

        Assert.False(result.Failed);
        Assert.Equal($"[{instance.Ticket}] failed: broken", result.Message);
    }

    [Fact]
    public async Task ShouldFail_WhenHelpCmdHasNoTargetOrAnUnknownOne()
    {
        var help = new ScriptHelpCommand(() => []);

        Assert.Equal("usage: help-cmd <command-name>", Failed(await Run(help)));
        Assert.StartsWith("No scripted command found for 'nope'.", Failed(await Run(help, "nope")), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldBelongToTheRunnerGroup_ForEveryBuiltIn()
    {
        IInteractiveCommand[] builtIns =
            [new PsCommand(_registry), new InspectCommand(_registry), new ResultCommand(_registry), Cancel(), new ScriptHelpCommand(() => [])];

        Assert.All(builtIns, c => Assert.Equal("runner", c.Group));
    }

    private static string? Failed(CommandResult result)
    {
        Assert.True(result.Failed);
        return result.Message;
    }
}
