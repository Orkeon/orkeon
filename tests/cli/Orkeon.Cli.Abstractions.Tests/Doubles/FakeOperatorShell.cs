using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>An <see cref="IOperatorShell"/> that keeps the commands it was given and writes a configured line.</summary>
public sealed class FakeOperatorShell : IOperatorShell
{
    /// <summary>The sentence returned as <see cref="DisabledReason"/>; null keeps the shell on.</summary>
    public string? Disabled { get; set; }

    /// <summary>Written to the console for each command.</summary>
    public string Prints { get; set; } = "ran";

    /// <summary>When set, called once the command started; the run then waits on its token until it is cancelled.</summary>
    public Action? Started { get; set; }

    /// <summary>The commands run, in order.</summary>
    public List<string> Commands { get; } = [];

    /// <inheritdoc />
    public string? DisabledReason => Disabled;

    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(string command, IConsoleAdapter console, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        console.WriteLine(Prints);
        if (Started is { } started)
        {
            started();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return CommandResult.Continue();
    }
}
