namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Production adapter that delegates to System.Console.
/// </summary>
public sealed class SystemConsoleAdapter : IConsoleAdapter
{
    public void Write(string text) => System.Console.Write(text);
    public void WriteLine(string text) => System.Console.WriteLine(text);
    public string? ReadLine() => System.Console.ReadLine();
    public ConsoleKeyInfo ReadKey(bool intercept = false) => System.Console.ReadKey(intercept);
    public void Clear() => System.Console.Clear();
}
