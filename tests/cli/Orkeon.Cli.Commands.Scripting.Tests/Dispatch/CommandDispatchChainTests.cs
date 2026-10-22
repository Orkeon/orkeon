using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// RT-20: a <c>completed</c> handler that launches a further background run keeps the
/// command's ambient scope, so the chained run is attributed to the command and its own
/// completion drains to it.
/// </summary>
[Collection("PromptScope")]
public sealed class CommandDispatchChainTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _crewsDir;
    private readonly List<IDisposable> _disposables = new();

    public CommandDispatchChainTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-chain-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _crewsDir = _tempDir + "-crews";
        Directory.CreateDirectory(_crewsDir);
    }

    public void Dispose()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* best effort */ } }
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_crewsDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("BR", "RT-20")]
    public async Task ShouldReturnTicket_WhenRunCrewAsyncCalledFromCompleted()
    {
        var service = NewService();
        var cmd = Load(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("chain-async hello", "hello", console), CancellationToken.None);
        await WaitUntil(() => console.Output.Contains("chained:", StringComparison.Ordinal));

        var line = console.Output.Split('\n').First(l => l.Contains("chained:", StringComparison.Ordinal));
        var ticket = line[(line.IndexOf("chained:", StringComparison.Ordinal) + "chained:".Length)..].Trim();
        var instance = service.Registry.Get(ticket);
        Assert.NotNull(instance);
        Assert.Equal("chain-async", instance!.Name);

        // Wired to drain: the terminal callback prints the host feedback line for its ticket.
        await WaitUntil(() => console.Output.Contains($"[{ticket}]", StringComparison.Ordinal));
        Assert.Contains($"[{ticket}]", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-20")]
    public async Task ShouldDrainSecondCrewCompletionToItsCommand_WhenChained()
    {
        var service = NewService();
        var cmd = Load(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("chain-async hello", "hello", console), CancellationToken.None);

        await WaitUntil(() => console.Output.Contains("second-done:two-out", StringComparison.Ordinal));
        Assert.Contains("second-done:two-out", console.Output, StringComparison.Ordinal);
    }

    private static CommandDispatchService NewService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry());

    private IInteractiveCommand Load(CommandDispatchService service) => ChainHarness.Load(service, _tempDir, _crewsDir);

    private static CommandContext Ctx(string raw, string arg, IConsoleAdapter console) => ChainHarness.Ctx(raw, arg, console);

    private static Task WaitUntil(Func<bool> predicate) => ChainHarness.WaitUntil(predicate);
}
