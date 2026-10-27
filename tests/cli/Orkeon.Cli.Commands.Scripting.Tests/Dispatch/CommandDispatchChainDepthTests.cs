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
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Communication;
using Orkeon.Scripting;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Dispatch;

/// <summary>
/// RT-20: the scope of a <c>completed</c> handler inherits the depth of its command's scope
/// plus one, so a chain longer than the limit is refused.
/// </summary>
[Collection("PromptScope")]
public sealed class CommandDispatchChainDepthTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _crewsDir;
    private readonly List<IDisposable> _disposables = new();

    public CommandDispatchChainDepthTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-chain-depth-" + Guid.NewGuid().ToString("N"));
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
    public async Task ShouldInheritDepth_WhenInstanceLaunchedFromCompleted()
    {
        var service = NewService(); // limit 1: the command (depth 0) may launch, its completed handler (depth 1) may not
        var cmd = Load(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("chain-async hello", "hello", console), CancellationToken.None);
        await WaitUntil(() => console.Output.Contains("refused:", StringComparison.Ordinal) || console.Output.Contains("chained:", StringComparison.Ordinal));

        Assert.DoesNotContain("chained:", console.Output, StringComparison.Ordinal);
        var line = console.Output.Split('\n').Single(l => l.Contains("refused:", StringComparison.Ordinal)).TrimEnd('\r');
        Assert.Equal("refused:chain depth 1 reached (MaxChainDepth 1, /chain-async)", line);
    }

    [Fact]
    [Trait("BR", "#221")]
    public async Task ShouldContinuePastTheLimit_WhenTheDriverRelaunchesItselfWithChainContinue()
    {
        var service = NewService(maxChainDepth: 2);
        var cmd = LoadLoop(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("loop-async continue", "continue", console), CancellationToken.None);
        await WaitUntil(() => console.Output.Contains("loop-done:", StringComparison.Ordinal) || console.Output.Contains("refused:", StringComparison.Ordinal));

        Assert.DoesNotContain("refused:", console.Output, StringComparison.Ordinal);
        Assert.Contains("loop-done:6", console.Output, StringComparison.Ordinal);
        var hops = service.Registry.List(CommandInstanceFilter.All);
        Assert.Equal(6, hops.Count);
        Assert.All(hops, h => Assert.Equal(1, service.Registry.Get(h.ticket)!.Depth));
    }

    [Fact]
    [Trait("BR", "#221")]
    public async Task ShouldRefuseAtTheLimitNamingIt_WhenTheSameLoopRelaunchesWithoutChainContinue()
    {
        var service = NewService(maxChainDepth: 2);
        var cmd = LoadLoop(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("loop-async climb", "climb", console), CancellationToken.None);
        await WaitUntil(() => console.Output.Contains("loop-done:", StringComparison.Ordinal) || console.Output.Contains("refused:", StringComparison.Ordinal));

        Assert.DoesNotContain("loop-done:", console.Output, StringComparison.Ordinal);
        var line = console.Output.Split('\n').Single(l => l.Contains("refused:", StringComparison.Ordinal)).TrimEnd('\r');
        Assert.Equal("refused:chain depth 2 reached (MaxChainDepth 2, /loop-async)", line);
    }

    [Fact]
    [Trait("BR", "#221")]
    public void ShouldStillClimb_WhenChainContinueIsAskedOutsideACompleted()
    {
        var service = NewService(maxChainDepth: 2);
        static Task<CommandResponse> Work(CancellationToken _) => Task.FromResult(new CommandResponse("a", "i", success: true, payload: "p", error: null));

        string ticket;
        using (service.BeginCommand("dev-batches", CancellationToken.None, parentDepth: 1))
            ticket = service.postWork("crew", "crew:x", Work, continueChain: true);
        Assert.Equal(2, service.Registry.Get(ticket)!.Depth);

        using var scope = service.BeginCommand("dev-batches", CancellationToken.None, parentDepth: 2);
        var refused = Assert.Throws<ChainDepthExceededException>(() => service.postWork("crew", "crew:x", Work, continueChain: true));
        Assert.Equal("chain depth 2 reached (MaxChainDepth 2, /dev-batches)", refused.Message);
    }

    [Fact]
    [Trait("BR", "#221")]
    public void ShouldKeepTheParentDepth_WhenChainContinueIsAskedFromACompleted()
    {
        var service = NewService(maxChainDepth: 2);
        static Task<CommandResponse> Work(CancellationToken _) => Task.FromResult(new CommandResponse("a", "i", success: true, payload: "p", error: null));

        using var scope = service.BeginCommand("dev-batches", CancellationToken.None, parentDepth: 2, fromCompleted: true);
        var ticket = service.postWork("crew", "crew:x", Work, continueChain: true);

        Assert.Equal(2, service.Registry.Get(ticket)!.Depth);
    }

    [Fact]
    [Trait("BR", "#221")]
    public void ShouldClimbOneHop_WhenCommandsPostIsCalledFromACompleted()
    {
        var service = NewEchoService(maxChainDepth: 4);

        using var scope = service.BeginCommand("askbg", CancellationToken.None, parentDepth: 2, fromCompleted: true);
        var ticket = service.post("echo", "run", "x");

        Assert.Equal(3, service.Registry.Get(ticket)!.Depth);
    }

    [Fact]
    [Trait("BR", "#221")]
    public async Task ShouldRefuseCommandsPostAtTheLimitNamingIt_WhenEachCompletedPostsAgain()
    {
        var service = NewEchoService(maxChainDepth: 2);
        var cmd = ChainHarness.Load(service, _tempDir, _crewsDir, "post-chain.cmd.ts", "post-chain",
            whitelist => whitelist.AddOptional("commands", _ => service));
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(Ctx("post-chain go", "go", console), CancellationToken.None);
        await WaitUntil(() => console.Output.Contains("refused:", StringComparison.Ordinal) || console.Output.Contains("unbounded:", StringComparison.Ordinal));

        Assert.DoesNotContain("unbounded:", console.Output, StringComparison.Ordinal);
        var line = console.Output.Split('\n').Single(l => l.Contains("refused:", StringComparison.Ordinal)).TrimEnd('\r');
        Assert.Equal("refused:chain depth 2 reached (MaxChainDepth 2, /post-chain)", line);
        Assert.Equal(2, service.Registry.List(CommandInstanceFilter.All).Count);
    }

    private CommandDispatchService NewEchoService(int maxChainDepth)
    {
        var service = NewService(maxChainDepth);
        var echo = AgentId.Create();
        _disposables.Add(service.Directory.Register("echo", echo));
        _disposables.Add(service.Channel.RegisterHandler(echo, (req, _) =>
            Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, echo, "echoed"))));
        return service;
    }

    private static CommandDispatchService NewService(int maxChainDepth = 1)
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry(),
               maxChainDepth: maxChainDepth);

    private IInteractiveCommand LoadLoop(CommandDispatchService service)
        => ChainHarness.Load(service, _tempDir, _crewsDir, "loop-async.cmd.ts", "loop-async");

    private IInteractiveCommand Load(CommandDispatchService service) => ChainHarness.Load(service, _tempDir, _crewsDir);

    private static CommandContext Ctx(string raw, string arg, IConsoleAdapter console) => ChainHarness.Ctx(raw, arg, console);

    private static Task WaitUntil(Func<bool> predicate) => ChainHarness.WaitUntil(predicate);
}
