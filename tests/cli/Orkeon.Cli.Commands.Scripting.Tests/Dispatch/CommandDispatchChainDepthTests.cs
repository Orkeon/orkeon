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
        var line = console.Output.Split('\n').Single(l => l.Contains("refused:", StringComparison.Ordinal));
        Assert.Contains("depth 1", line, StringComparison.Ordinal);
        Assert.Contains("limit 1", line, StringComparison.Ordinal);
    }

    private static CommandDispatchService NewService()
        => new(new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
               new AgentCommandDirectory(),
               new CommandInstanceRegistry(),
               maxChainDepth: 1);

    private IInteractiveCommand Load(CommandDispatchService service) => ChainHarness.Load(service, _tempDir, _crewsDir);

    private static CommandContext Ctx(string raw, string arg, IConsoleAdapter console) => ChainHarness.Ctx(raw, arg, console);

    private static Task WaitUntil(Func<bool> predicate) => ChainHarness.WaitUntil(predicate);
}
