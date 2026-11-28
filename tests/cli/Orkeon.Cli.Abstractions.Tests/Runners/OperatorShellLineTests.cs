using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Abstractions.Tests.Doubles;
using Orkeon.Cli.Abstractions.Tests.Fixtures;

namespace Orkeon.Cli.Abstractions.Tests.Runners;

/// <summary>
/// A line that starts with <c>!</c> (#491): run by the <see cref="IOperatorShell"/> when the operator typed it, refused
/// with a sentence otherwise; <c>\!</c> is a prompt that starts with <c>!</c>.
/// </summary>
public sealed class OperatorShellLineTests
{
    private readonly FakeOperatorShell _shell = new();
    private readonly RecordingCommand _assistant = new("assistant");
    private readonly List<CommandOutcome> _outcomes = [];

    private TestRunner Build(StubLineConsole console, bool withShell = true, bool slashOnly = true)
    {
        var services = new ServiceCollection();
        if (withShell)
            services.AddSingleton<IOperatorShell>(_shell);
        services.AddSingleton<ICommandOutcomeObserver>(new RecordingObserver(_outcomes));
        return new TestRunner(
            new TestCommandRegistry(),
            TestCommandRegistry.WithFallback(_assistant),
            console,
            services.BuildServiceProvider())
        { SlashOnly = slashOnly };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATypedLine_RunsTheCommand_WithoutATurnOfTheModel(bool slashOnly)
    {
        var console = new StubLineConsole("! echo a | tr a b");

        await Build(console, slashOnly: slashOnly).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["echo a | tr a b"], _shell.Commands);
        Assert.Empty(_assistant.Invocations);
        Assert.Contains("ran", console.Output);
        Assert.Equal(CommandOutcomeKind.Succeeded, Assert.Single(_outcomes).Kind);
    }

    [Fact]
    public async Task AReplayedLine_IsRefusedWithASentence()
    {
        var console = new StubLineConsole("! git status") { Replayed = true };

        await Build(console).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_shell.Commands);
        Assert.Empty(_assistant.Invocations);
        Assert.Contains(InteractiveRunnerBase.OperatorShellReplayedRefusal, console.Output);
        var outcome = Assert.Single(_outcomes);
        Assert.Equal(CommandOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(InteractiveRunnerBase.OperatorShellReplayedRefusal, outcome.Message);
    }

    [Fact]
    public async Task WhenTheShellIsOff_TheLineIsRefusedWithItsSentence()
    {
        _shell.Disabled = "Refused: off (Orkeon:Cli:Repl:ShellPrefix:Enabled is false).";
        var console = new StubLineConsole("! ls");

        await Build(console).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_shell.Commands);
        Assert.Contains("Orkeon:Cli:Repl:ShellPrefix:Enabled is false", console.Output);
        Assert.Equal(CommandOutcomeKind.Failed, Assert.Single(_outcomes).Kind);
    }

    [Fact]
    public async Task WithoutAShell_TheLineIsRefused_AndNeverReachesTheModel()
    {
        var console = new StubLineConsole("! ls");

        await Build(console, withShell: false).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_assistant.Invocations);
        Assert.Contains(InteractiveRunnerBase.OperatorShellMissingRefusal, console.Output);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("!   ")]
    [InlineData("!!")]
    [InlineData("!!ls")]
    public async Task ALineWithNoCommand_PrintsTheUsage(string line)
    {
        var console = new StubLineConsole(line);

        await Build(console).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_shell.Commands);
        Assert.Empty(_assistant.Invocations);
        Assert.Contains(InteractiveRunnerBase.OperatorShellUsage, console.Output);
    }

    [Fact]
    public async Task AnEscapedBang_IsAPromptThatStartsWithABang()
    {
        var console = new StubLineConsole("\\!important: read this");

        await Build(console).RunAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_shell.Commands);
        Assert.Equal("!important: read this", Assert.Single(_assistant.Invocations).RawInput);
    }

    [Fact]
    public async Task TheCommand_IsCancelledAsATurnIs()
    {
        var console = new StubLineConsole("! sleep 100");
        var runner = Build(console);
        _shell.Started = runner.RequestCommandCancellation;

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(_shell.Commands);
        Assert.Contains("Command '!' cancelled.", console.Output);
        Assert.Equal(CommandOutcomeKind.Cancelled, Assert.Single(_outcomes).Kind);
    }

    private sealed class RecordingObserver(List<CommandOutcome> outcomes) : ICommandOutcomeObserver
    {
        public void OnCommandOutcome(CommandOutcome outcome) => outcomes.Add(outcome);
    }
}
