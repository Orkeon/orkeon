using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Registry;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Fixtures;

/// <summary>Minimal runner that exposes hooks for assertion in tests.</summary>
public sealed class TestRunner : InteractiveRunnerBase
{
    public TestRunner(
        IInteractiveCommandRegistry defaults,
        IInteractiveCommandRegistry specific,
        IConsoleAdapter console,
        IServiceProvider services,
        ILogger? logger = null)
        : base(defaults, specific, console, logger ?? NullLogger.Instance, services)
    {
    }

    protected override string Banner => "TEST";
    protected override string Prompt => "test> ";

    /// <summary>Toggle the slash-only grammar (cap 1/5) for dispatch tests. Default off (legacy).</summary>
    public bool SlashOnly { get; set; }

    protected override bool SlashCommandsOnly => SlashOnly;

    public int OnStartCount { get; private set; }
    public int OnExitCount { get; private set; }
    public CommandResult? LastResultSeen { get; private set; }
    public IInteractiveCommandRegistry? RegistrySnapshotDuringRun { get; private set; }

    protected override Task OnStartAsync(CancellationToken ct)
    {
        OnStartCount++;
        RegistrySnapshotDuringRun = RunnerContext.Current;
        return Task.CompletedTask;
    }

    protected override Task OnExitAsync(CommandResult? lastResult, CancellationToken ct)
    {
        OnExitCount++;
        LastResultSeen = lastResult;
        return Task.CompletedTask;
    }

    /// <summary>Expose the protected parser for direct testing.</summary>
    public (string Name, IReadOnlyList<string> Args) ParseForTest(string input) => ParseCommandName(input);

    /// <summary>Expose the specific registry for assertions.</summary>
    public IInteractiveCommandRegistry SpecificForTest => Specific;
}
