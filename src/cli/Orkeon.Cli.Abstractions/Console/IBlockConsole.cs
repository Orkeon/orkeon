namespace Orkeon.Cli.Abstractions.Console;

/// <summary>What a block of text is, so that a console able to style it knows how to.</summary>
public enum ConsoleBlockKind
{
    /// <summary>Markdown source.</summary>
    Markdown,

    /// <summary>A unified diff.</summary>
    Diff,
}

/// <summary>
/// A console that renders typed blocks of text. The caller hands over the source and never a colour: a
/// console without this capability shows the same source as plain lines.
/// </summary>
public interface IBlockConsole
{
    /// <summary>Writes <paramref name="text"/> on lines of their own, styled after <paramref name="kind"/>.</summary>
    void WriteBlock(ConsoleBlockKind kind, string text);
}
