using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// #220: a <c>completed</c> handler may return a promise, and the drain awaits it — a host read, then the launch of
/// the next crew, both run inside the drain, so the launch is chained to the command like one made synchronously.
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandAsyncCompletedTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-async-completed-" + Guid.NewGuid().ToString("N"));
    private readonly string _crewsDir;
    private readonly StubSessionStore _session = new();

    public ScriptCommandAsyncCompletedTests()
    {
        Directory.CreateDirectory(_tempDir);
        _crewsDir = _tempDir + "-crews";
        Directory.CreateDirectory(_crewsDir);
    }

    public void Dispose()
    {
        _session.Mode.TrySetResult("released");
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_crewsDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ShouldAwaitTheCompletedPromise_WhenItReadsTheSessionStoreThenLaunchesACrew()
    {
        var service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry());
        var cmd = ChainHarness.Load(service, _tempDir, _crewsDir, "async-completed.cmd.ts", "async-completed",
            whitelist => whitelist.AddOptional("session", _ => _session));
        var console = new ScriptedTestConsole();

        await cmd.ExecuteAsync(ChainHarness.Ctx("async-completed go", "go", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => _session.Reads == 1);

        // The handler is suspended on the read: the drain still holds its work, nothing after the await ran.
        Assert.True(service.Registry.HasRunning);
        Assert.DoesNotContain("mode:", console.Output, StringComparison.Ordinal);

        _session.Mode.SetResult("plan");
        await ChainHarness.WaitUntil(() => console.Output.Contains("second-done:", StringComparison.Ordinal));

        var lines = console.Output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var mode = lines.FindIndex(l => l == "mode:plan");
        var launched = lines.FindIndex(l => l.StartsWith("launched:", StringComparison.Ordinal));
        Assert.True(mode >= 0 && launched > mode, console.Output);
        Assert.Contains("second-done:two-out", lines);

        // The crew launched after the await belongs to the command and was chained one hop deeper.
        var second = service.Registry.Get(lines[launched]["launched:".Length..]);
        Assert.NotNull(second);
        Assert.Equal("async-completed", second.Name);
        Assert.Equal(2, second.Depth);
    }
}
