using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// Runs a shell command the operator typed at the prompt after <c>!</c> (<c>! git status</c>): the operator's own
/// shell, outside every rule made for a model's tool call. Resolved from the runner's services by
/// <see cref="InteractiveRunnerBase"/>; a runner whose services hold none refuses the line.
/// </summary>
public interface IOperatorShell
{
    /// <summary>The sentence printed in place of running a command when the feature is turned off; null when it is on.</summary>
    string? DisabledReason { get; }

    /// <summary>Runs <paramref name="command"/>, writing its output to <paramref name="console"/> as it comes.</summary>
    /// <param name="command">The command line, without the leading <c>!</c>.</param>
    /// <param name="console">The console the output is written to.</param>
    /// <param name="cancellationToken">Cancelled by Esc or Ctrl+C, as a command's turn is.</param>
    System.Threading.Tasks.Task<CommandResult> RunAsync(string command, IConsoleAdapter console, CancellationToken cancellationToken);
}
