using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>An <see cref="IOperatorShell"/>, on, that keeps the commands it was given.</summary>
internal sealed class FakeOperatorShell : IOperatorShell
{
    public List<string> Commands { get; } = [];

    public string? DisabledReason => null;

    public Task<CommandResult> RunAsync(string command, IConsoleAdapter console, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        return Task.FromResult(CommandResult.Continue());
    }
}
