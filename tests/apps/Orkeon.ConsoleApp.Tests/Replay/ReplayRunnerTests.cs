using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>
/// A replay driven through a real read loop (<see cref="InteractiveRunnerBase"/>): the runner reports each
/// command's outcome, the replay stops at the first failure and gives exit code 1.
/// </summary>
public sealed class ReplayRunnerTests : IDisposable
{
    private readonly FakeConsoleAdapter _inner = new();
    private readonly ReplayIdleGate _gate = new(() => new CommandInstanceRegistry(), () => null);
    private readonly ReplayConsoleAdapter _replay;
    private readonly StubCommand _ok = new("ok", CommandResult.Continue("fine"));
    private readonly StubCommand _broken = new("broken", CommandResult.Fail("Error: the script threw"));

    public ReplayRunnerTests() => _replay = new ReplayConsoleAdapter(_inner, _gate);

    public void Dispose() => _gate.Dispose();

    private ReplayTestRunner Build()
    {
        var services = new ServiceCollection()
            .AddSingleton<ICommandOutcomeObserver>(_replay)
            .BuildServiceProvider();
        return new ReplayTestRunner(
            new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand()),
            new StubCommandRegistry(_ok, _broken),
            _replay,
            NullLogger.Instance,
            services);
    }

    private static ReplayLine[] Lines(params string[] texts)
        => texts.Select((text, index) => new ReplayLine(index + 1, text)).ToArray();

    [Fact]
    public async Task ShouldStopAtTheFirstFailure_AndReportItsLine()
    {
        _replay.TryStart(Lines("ok", "broken", "ok"), closeInputAtEnd: true);

        await Build().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _ok.Calls);
        Assert.Equal(1, _broken.Calls);
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.Conclude());
        Assert.True(_inner.Contains("replay stopped at line 2: Error: the script threw"));
    }

    [Fact]
    public async Task ShouldStop_WhenALineIsAnUnknownCommand()
    {
        _replay.TryStart(Lines("ok", "nonsense", "ok"), closeInputAtEnd: true);

        await Build().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _ok.Calls);
        Assert.Equal(ReplayConsoleAdapter.FailedExitCode, _replay.Conclude());
        Assert.True(_inner.Contains("replay stopped at line 2: unknown command 'nonsense'"));
    }

    [Fact]
    public async Task ShouldExitZero_WhenEveryLinePasses()
    {
        _replay.TryStart(Lines("ok", "ok"), closeInputAtEnd: true);

        await Build().RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _ok.Calls);
        Assert.Equal(ReplayConsoleAdapter.PassedExitCode, _replay.Conclude());
        Assert.False(_inner.Contains("replay stopped"));
    }

    private sealed class ReplayTestRunner(
        IInteractiveCommandRegistry defaults,
        IInteractiveCommandRegistry specific,
        IConsoleAdapter console,
        ILogger logger,
        IServiceProvider services) : InteractiveRunnerBase(defaults, specific, console, logger, services)
    {
        protected override string Banner => "replay test";

        protected override string Prompt => "> ";
    }

    private sealed class StubCommandRegistry(params IInteractiveCommand[] commands) : IInteractiveCommandRegistry
    {
        public IEnumerable<IInteractiveCommand> Commands => commands;

        public IInteractiveCommand? Fallback => null;
    }

    private sealed class StubCommand(string name, CommandResult result) : IInteractiveCommand
    {
        public int Calls { get; private set; }

        public string Name => name;

        public IReadOnlyList<string> Aliases => [];

        public string Description => name;

        public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
