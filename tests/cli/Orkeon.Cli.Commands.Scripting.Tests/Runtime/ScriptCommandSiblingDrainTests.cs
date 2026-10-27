using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// The commands of one file share a completion drain queue; a command pumping it takes only the completions of
/// the runs keyed to its own name, so a sibling's <c>completed</c> never runs as (nor chains its launches to) the
/// wrong command.
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandSiblingDrainTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-sibling-drain-" + Guid.NewGuid().ToString("N"));
    private readonly CommandDispatchService _service;
    private readonly GatedScriptHost _host;
    private readonly ScriptedTestConsole _console = new();

    public ScriptCommandSiblingDrainTests()
    {
        Directory.CreateDirectory(_tempDir);
        _service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry());
        _host = new GatedScriptHost(_service);
    }

    public void Dispose()
    {
        _host.OpenAll();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ShouldDrainTheCompletionToItsOwnCommand_WhenASiblingPumpsTheSharedQueueFirst()
    {
        var all = ChainHarness.LoadAll(_service, _tempDir, "sibling.cmd.ts",
            new ScriptServiceWhitelist().AddOptional("script-host", _ => _host));
        var a = all.Single(c => c.Name == "sibling-a");
        var b = all.Single(c => c.Name == "sibling-b");
        _host.Gate("b-run").TrySetResult();

        await a.ExecuteAsync(ChainHarness.Ctx("sibling-a go", "go", _console), CancellationToken.None);

        // B1 parks its dispatch holding the shared engine lock; B2 queues on the lock before A's push drain.
        var b1 = Task.Run(() => b.ExecuteAsync(ChainHarness.Ctx("sibling-b park", "p", _console), CancellationToken.None));
        await ChainHarness.WaitUntil(() => _host.Gate("park-entered").Task.IsCompleted);
        var b2 = b.ExecuteAsync(ChainHarness.Ctx("sibling-b two", "t", _console), CancellationToken.None);

        _host.Gate("a-run").TrySetResult();
        await ChainHarness.WaitUntil(() => _console.Output.Contains("crew:a-run: done", StringComparison.Ordinal));
        _host.Gate("park").TrySetResult();
        await b1;
        await b2;
        await ChainHarness.WaitUntil(() => _console.Output.Contains("a-completed:", StringComparison.Ordinal));

        var lines = _console.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains("a-completed:a-run-out as sibling-a", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("a-completed:", StringComparison.Ordinal) && l.EndsWith("as sibling-b", StringComparison.Ordinal));
    }
}
