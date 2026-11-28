using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Communication;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// #493: <c>completed(result)</c> receives the ticket of the instance that ended and the <c>state</c> its launch
/// attached, so two instances of one command can be in flight at once without a shared note in the session.
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandLaunchStateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _crewsDir;
    private readonly List<IDisposable> _disposables = new();
    private GatedScriptHost? _gated;

    public ScriptCommandLaunchStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-launch-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _crewsDir = _tempDir + "-crews";
        Directory.CreateDirectory(_crewsDir);
    }

    public void Dispose()
    {
        _gated?.OpenAll();
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* best effort */ } }
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_crewsDir, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("a", "b")]
    [InlineData("b", "a")]
    [Trait("BR", "#493")]
    public async Task ShouldHandEachCompletedItsOwnTicketAndState_WhenTwoInstancesEndInEitherOrder(string first, string second)
    {
        var service = NewService();
        _gated = new GatedScriptHost(service);
        var host = _gated;
        var cmd = ChainHarness.LoadAll(service, _tempDir, "launch-state.cmd.ts",
            new ScriptServiceWhitelist().AddOptional("script-host", _ => host)).Single(c => c.Name == "stateful");
        var console = new ScriptedTestConsole();

        var launchA = await cmd.ExecuteAsync(ChainHarness.Ctx("stateful a", "a", console), CancellationToken.None);
        var launchB = await cmd.ExecuteAsync(ChainHarness.Ctx("stateful b", "b", console), CancellationToken.None);
        Assert.False(launchA.Failed);
        Assert.False(launchB.Failed);
        var tickets = new Dictionary<string, string> { ["a"] = TicketOf(console, "a"), ["b"] = TicketOf(console, "b") };
        Assert.NotEqual(tickets["a"], tickets["b"]);

        _gated.Gate(first).TrySetResult();
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:" + first + ":", StringComparison.Ordinal));
        _gated.Gate(second).TrySetResult();
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:" + second + ":", StringComparison.Ordinal)
            && console.Output.Contains("launched:c:", StringComparison.Ordinal));

        Assert.Equal($"completed:a:{tickets["a"]}:{{\"turn\":\"a\"}}", Line(console, "completed:a:"));
        Assert.Equal($"completed:b:{tickets["b"]}:{{\"turn\":\"b\"}}", Line(console, "completed:b:"));
        var lines = Lines(console);
        Assert.True(lines.IndexOf(Line(console, "completed:" + first + ":")) < lines.IndexOf(Line(console, "completed:" + second + ":")));

        // The launch chained from a's completed carries the state it was given, not a's.
        var chained = TicketOf(console, "c");
        _gated.Gate("c").TrySetResult();
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:c:", StringComparison.Ordinal));
        Assert.Equal($"completed:c:{chained}:{{\"turn\":\"c\",\"from\":\"{tickets["a"]}\"}}", Line(console, "completed:c:"));
    }

    [Fact]
    [Trait("BR", "#493")]
    public async Task ShouldHandBackAStructuredState_WhenTheRealFacadeLaunchesWithOne()
    {
        var service = NewService();
        var cmd = LoadFacade(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("facade-state plain", "plain", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:", StringComparison.Ordinal));

        var ticket = Line(console, "launched:")["launched:".Length..];
        Assert.Equal($"completed:{ticket}:{{\"n\":1,\"list\":[\"x\",2,null]}}", Line(console, "completed:"));
    }

    [Fact]
    [Trait("BR", "#493")]
    public async Task ShouldLeaveStateUndefinedAndStillNameTheTicket_WhenTheLaunchAttachesNone()
    {
        var service = NewService();
        var cmd = LoadFacade(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("facade-state none", "none", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:", StringComparison.Ordinal));

        var ticket = Assert.Single(service.Registry.List(CommandInstanceFilter.All)).ticket;
        Assert.Equal($"completed:{ticket}:undefined", Line(console, "completed:"));
    }

    [Fact]
    [Trait("BR", "#493")]
    public async Task ShouldRefuseTheLaunchNamingTheCap_WhenTheStateIsAbove16KiB()
    {
        var service = NewService();
        var cmd = LoadFacade(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("facade-state big", "big", console), CancellationToken.None);

        Assert.Equal(
            "refused:script-host.runCrewAsync: options.state is 16395 bytes once serialised, above the cap of 16384 bytes (16 KiB).",
            Line(console, "refused:"));
        Assert.Empty(service.Registry.List(CommandInstanceFilter.All));
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("fn")]
    [Trait("BR", "#493")]
    public async Task ShouldRefuseTheLaunch_WhenTheStateIsNotSerialisable(string mode)
    {
        var service = NewService();
        var cmd = LoadFacade(service);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("facade-state " + mode, mode, console), CancellationToken.None);

        Assert.StartsWith("refused:script-host.runCrewAsync: options.state must be a JSON-serialisable value", Line(console, "refused:"), StringComparison.Ordinal);
        Assert.DoesNotContain("launched", console.Output, StringComparison.Ordinal);
        Assert.Empty(service.Registry.List(CommandInstanceFilter.All));
    }

    [Fact]
    [Trait("BR", "#493")]
    public async Task ShouldHandBackThePostState_WhenCommandsPostAttachesOne()
    {
        var service = NewService();
        var echo = AgentId.Create();
        _disposables.Add(service.Directory.Register("echo", echo));
        _disposables.Add(service.Channel.RegisterHandler(echo, (req, _) =>
            Task.FromResult(AgentChannelResponse.Ok(req.CorrelationId, echo, "echoed"))));
        var cmd = LoadFacade(service, withCommands: true);
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("facade-state post", "post", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => console.Output.Contains("completed:", StringComparison.Ordinal));

        var ticket = Assert.Single(service.Registry.List(CommandInstanceFilter.All)).ticket;
        Assert.Equal($"completed:{ticket}:{{\"via\":\"post\"}}", Line(console, "completed:"));
    }

    private IInteractiveCommand LoadFacade(CommandDispatchService service, bool withCommands = false)
        => ChainHarness.Load(service, _tempDir, _crewsDir, "facade-state.cmd.ts", "facade-state",
            withCommands ? whitelist => whitelist.AddOptional("commands", _ => service) : null);

    private static CommandDispatchService NewService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry(),
               maxChainDepth: 4);

    private static List<string> Lines(ScriptedTestConsole console)
        => console.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    private static string Line(ScriptedTestConsole console, string prefix)
        => Lines(console).Single(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static string TicketOf(ScriptedTestConsole console, string crew)
        => Line(console, "launched:" + crew + ":")[("launched:" + crew + ":").Length..];
}
