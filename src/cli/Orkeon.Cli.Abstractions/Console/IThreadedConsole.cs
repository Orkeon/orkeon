namespace Orkeon.Cli.Abstractions.Console;

/// <summary>A console that keeps the output of separate threads (background tasks) apart.</summary>
public interface IThreadedConsole
{
    bool SeparatesThreads { get; }
    void WriteLine(string? thread, string text);
    void WriteAttention(string? thread, string text);
}
