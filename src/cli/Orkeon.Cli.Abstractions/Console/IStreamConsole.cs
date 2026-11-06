namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// A console that tells streamed text from everything else. <see cref="IConsoleAdapter.Write"/> may
/// read its text as a prompt (the split-pane console does, on a <c>&gt;</c> at the end), which a model's
/// token must never be: a streamed delta ending in <c>&gt;</c> is text of the answer.
/// </summary>
public interface IStreamConsole
{
    /// <summary>Appends streamed text to the output as it is.</summary>
    void WriteStream(string text);
}
