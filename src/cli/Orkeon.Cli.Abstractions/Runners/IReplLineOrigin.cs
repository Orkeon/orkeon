namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// A console that states where the line it last returned came from. The read loop of
/// <see cref="InteractiveRunnerBase"/> runs a shell command of the operator's own (a line that starts with
/// <c>!</c>) only for a line the console states was typed at an interactive terminal: a console that does not implement
/// this interface — piped input, a replay file, a feed of a host's own — never runs one.
/// </summary>
public interface IReplLineOrigin
{
    /// <summary>
    /// Whether the last line read was typed by the operator at an interactive terminal (the keyboard, not a redirected
    /// input or a replay file); false before any line was read.
    /// </summary>
    bool LastLineTyped { get; }
}
