using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

/// <summary>A console with palettes: keeps the one it is asked for, and writes nothing anywhere.</summary>
internal sealed class ScriptedThemeConsole : IConsoleAdapter, IThemeConsole
{
    public bool HasPalette { get; set; } = true;

    public ConsoleTheme Theme { get; private set; } = ConsoleTheme.Dark;

    public void SetTheme(ConsoleTheme theme) => Theme = theme;

    public void Write(string text) { }

    public void WriteLine(string text) { }

    public string? ReadLine() => null;

    public Task<string?> ReadLineAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    public ConsoleKeyInfo ReadKey(bool intercept) => default;

    public void Clear() { }
}
