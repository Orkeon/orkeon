namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// A question asked from outside the read loop — a crew running in the background asking to approve a
/// tool call — that the operator's next line answers. The loop stays the console's only reader: while a
/// question waits it shows <see cref="PromptOverride"/> instead of its own prompt, and it offers every line it
/// reads to <see cref="TryConsume"/> before resolving a command. Registered in the runner's services; a runner
/// without one reads as before.
/// </summary>
public interface IReplLineInterceptor
{
    /// <summary>The prompt to show instead of the runner's while a question waits; null when none does.</summary>
    string? PromptOverride { get; }

    /// <summary>Offers a line the loop read.</summary>
    /// <param name="line">The line as read.</param>
    /// <returns><see langword="true"/> when the line was taken for a waiting question: the loop then runs no command.</returns>
    bool TryConsume(string line);
}
