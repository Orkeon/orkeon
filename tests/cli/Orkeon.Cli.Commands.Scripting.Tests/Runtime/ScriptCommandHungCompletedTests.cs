using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>A <c>completed</c> whose promise never settles holds the engine until the user gives up waiting for it.</summary>
[Collection("PromptScope")]
public sealed class ScriptCommandHungCompletedTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hung-completed-" + Guid.NewGuid().ToString("N"));
    private readonly string _crewsDir;
    private readonly StubSessionStore _session = new();

    public ScriptCommandHungCompletedTests()
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
    public async Task Cancelling_the_invocation_that_waits_for_the_engine_ends_the_drain_that_holds_it()
    {
        // Arrange
        var service = new CommandDispatchService(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry());
        var cmd = ChainHarness.Load(service, _tempDir, _crewsDir, "async-completed.cmd.ts", "async-completed",
            whitelist => whitelist.AddOptional("session", _ => _session));
        var console = new ScriptedTestConsole();
        await cmd.ExecuteAsync(ChainHarness.Ctx("async-completed go", "go", console), CancellationToken.None);
        await ChainHarness.WaitUntil(() => _session.Reads == 1);
        using var waiting = new CancellationTokenSource();
        var next = cmd.ExecuteAsync(ChainHarness.Ctx("async-completed again", "again", console), waiting.Token);

        // Act
        await waiting.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        await ChainHarness.WaitUntil(() => !service.Registry.HasRunning);

        // Assert
        Assert.False(service.Registry.HasRunning);
    }
}
