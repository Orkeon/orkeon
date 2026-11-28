namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// A console that says where the line it last returned came from. The read loop of
/// <see cref="InteractiveRunnerBase"/> runs a shell command of the operator's own (a line that starts with
/// <c>!</c>) only for a line the operator typed: a console that feeds lines of its own — a replay file — implements
/// this interface, and a console that does not is a keyboard.
/// </summary>
public interface IReplLineOrigin
{
    /// <summary>Whether the last line read was not typed by the operator (it came from a replay file).</summary>
    bool LastLineReplayed { get; }
}
