using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// The end line of a background task and the output of its <c>completed</c> belong to the main thread: they run
/// when the task's own flow settles, yet the split-pane console must put them on <c>main</c>, never on the ended
/// task's screen (SPEC repl-transcript-per-task UC-03, BR-03).
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandCompletionThreadTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-completion-thread-" + Guid.NewGuid().ToString("N"));
    private readonly string _crewsDir;

    public ScriptCommandCompletionThreadTests()
    {
        Directory.CreateDirectory(_tempDir);
        _crewsDir = _tempDir + "-crews";
        Directory.CreateDirectory(_crewsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_crewsDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    [Trait("BR", "UC-03")]
    [Trait("BR", "BR-03")]
    public async Task ShouldWriteTheEndLineAndTheCompletedOutputOnMain_WhenABackgroundTaskEnds()
    {
        var service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry());
        var cmd = ChainHarness.Load(service, _tempDir, _crewsDir);
        var console = new FakeAmbientThreadConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("chain-async go", "go", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => console.Lines.Any(l => l.Text.StartsWith("second-done:", StringComparison.Ordinal)));

        var lines = console.Lines;
        var ends = lines.Where(l => l.Text.Contains("→ crew:", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, ends.Count);
        Assert.All(ends, l => Assert.Matches(@"^  ✓ \[t\d+\] chain-async → crew:(one|two): done$", l.Text));
        Assert.All(ends, l => Assert.Null(l.Thread));
        Assert.Contains(lines, l => l.Text.StartsWith("chained:", StringComparison.Ordinal) && l.Thread is null);
        Assert.Contains(lines, l => l.Text == "second-done:two-out" && l.Thread is null);
    }
}
