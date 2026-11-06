using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// Work launched with <c>{ onMain: true }</c> is the conversation: its end says something only when it failed. A
/// success has its reply on the screen, and an interruption was announced by the console when the keys were pressed.
/// </summary>
[Collection("PromptScope")]
public sealed class ScriptCommandOnMainCompletionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-on-main-" + Guid.NewGuid().ToString("N"));
    private readonly CommandDispatchService _service;
    private readonly GatedScriptHost _host;
    private readonly FakeAmbientThreadConsole _console = new();

    public ScriptCommandOnMainCompletionTests()
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

    private async Task<IInteractiveCommand> LaunchFirstAsync()
    {
        var command = ChainHarness.LoadAll(_service, _tempDir, "on-main.cmd.ts",
            new ScriptServiceWhitelist().AddOptional("script-host", _ => _host)).Single();
        await command.ExecuteAsync(ChainHarness.Ctx("on-main go", "go", _console), CancellationToken.None);
        return command;
    }

    private bool HasEndLine => _console.Lines.Any(l => l.Text.Contains("→ crew:", StringComparison.Ordinal));

    private Task WaitForLineAsync(string text) => ChainHarness.WaitUntil(() => _console.Lines.Any(l => l.Text == text));

    [Fact]
    public async Task ShouldWriteNoEndLineButRunCompleted_WhenWorkOnMainEndsWell()
    {
        await LaunchFirstAsync();

        _host.Gate("first").TrySetResult();
        await WaitForLineAsync("completed:True:first-out");

        Assert.False(HasEndLine);
    }

    [Fact]
    public async Task ShouldWriteTheEndLineAndRunCompleted_WhenWorkOnMainFails()
    {
        await LaunchFirstAsync();

        _host.Gate("first").TrySetException(new InvalidOperationException("boom"));
        await WaitForLineAsync("completed:False:");

        Assert.True(HasEndLine);
    }

    [Fact]
    public async Task ShouldWriteNothing_WhenWorkOnMainIsInterrupted()
    {
        var command = await LaunchFirstAsync();
        var first = _service.list().Single().ticket;

        Assert.True(_service.cancel(first));
        // The second launch's completion drains after the first's, so its line proves the first's drain has run.
        await command.ExecuteAsync(ChainHarness.Ctx("on-main go", "go", _console), CancellationToken.None);
        _host.Gate("second").TrySetResult();
        await WaitForLineAsync("completed:True:second-out");

        Assert.False(HasEndLine);
        Assert.Single(_console.Lines, l => l.Text.StartsWith("completed:", StringComparison.Ordinal));
    }
}
