namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>How a line the read loop handed to a command ended.</summary>
public enum CommandOutcomeKind
{
    /// <summary>The command returned a result that is not <see cref="Commands.CommandResult.Failed"/>.</summary>
    Succeeded,

    /// <summary>The command threw, or returned a <see cref="Commands.CommandResult.Failed"/> result.</summary>
    Failed,

    /// <summary>The command was cancelled (Ctrl-C, Esc) while the loop kept running.</summary>
    Cancelled,

    /// <summary>The line named no command and no fallback took it.</summary>
    Unknown,
}

/// <summary>
/// The outcome of one line the read loop resolved: what was typed, the command that ran it (null for an
/// unknown command), how it ended, and the message shown — the error for a failure.
/// </summary>
/// <param name="Input">The line as it was read.</param>
/// <param name="CommandName">The command that ran it; null when none matched.</param>
/// <param name="Kind">How it ended.</param>
/// <param name="Message">The result's message, the error or the refusal; null when there is none.</param>
public sealed record CommandOutcome(string Input, string? CommandName, CommandOutcomeKind Kind, string? Message);

/// <summary>
/// Told the outcome of every line an <see cref="InteractiveRunnerBase"/> resolves to a command, or fails to:
/// the hook a replay of the REPL listens to, to stop at the first error. Registered in the runner's services;
/// an observer that throws is logged and never stops the loop.
/// </summary>
public interface ICommandOutcomeObserver
{
    /// <summary>Called once per resolved line, after the command ended and its message was shown.</summary>
    /// <param name="outcome">The outcome.</param>
    void OnCommandOutcome(CommandOutcome outcome);
}
