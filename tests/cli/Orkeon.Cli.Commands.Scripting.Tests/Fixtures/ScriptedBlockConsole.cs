using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

/// <summary>A console that renders blocks: records them, and writes nothing else anywhere.</summary>
internal sealed class ScriptedBlockConsole : IConsoleAdapter, IBlockConsole
{
    public List<(ConsoleBlockKind Kind, string Text)> Blocks { get; } = [];

    public void Write(string text) { }

    public void WriteLine(string text) { }

    public string? ReadLine() => null;

    public Task<string?> ReadLineAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    public ConsoleKeyInfo ReadKey(bool intercept) => default;

    public void Clear() { }

    public void WriteBlock(ConsoleBlockKind kind, string text) => Blocks.Add((kind, text));
}
