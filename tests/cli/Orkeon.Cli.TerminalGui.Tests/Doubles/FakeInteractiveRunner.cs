using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.TerminalGui.Tests.Doubles;

internal sealed class FakeInteractiveRunner : IInteractiveRunner
{
    public bool Running;
    public int CancelRequests;

    public bool IsCommandRunning => Running;
    public string? CurrentCommandName => null;
    public DateTime? CurrentCommandStartedAtUtc => null;

    public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
    public void RequestCommandCancellation() => CancelRequests++;
}
