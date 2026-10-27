namespace Orkeon.Cli.Abstractions.Console;

/// <summary>Async-flow-local ticket of the writing thread; null for main.</summary>
public static class ConsoleThread
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
