using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// Records what is written. Reads are not scripted by default (a line reads <c>null</c>, a key reads
/// <c>default</c>); once <see cref="ScriptedLines"/> or <see cref="ScriptedKeys"/> is set, reads are
/// served from it and an exhausted script throws, so a prompt loop cannot spin forever.
/// </summary>
internal sealed class FakeConsoleAdapter : IConsoleAdapter
{
    public List<string> Lines { get; } = [];

    /// <summary>Lines served to <see cref="ReadLine"/> in order, when set.</summary>
    public Queue<string?>? ScriptedLines { get; set; }

    /// <summary>Keys served to <see cref="ReadKey"/> in order, when set.</summary>
    public Queue<ConsoleKey>? ScriptedKeys { get; set; }

    public void Write(string text) => Lines.Add(text);

    public void WriteLine(string text) => Lines.Add(text);

    public string? ReadLine()
    {
        if (ScriptedLines is null)
            return null;
        if (ScriptedLines.Count == 0)
            throw new InvalidOperationException("The scripted lines are exhausted.");
        return ScriptedLines.Dequeue();
    }

    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        if (ScriptedKeys is null)
            return default;
        if (ScriptedKeys.Count == 0)
            throw new InvalidOperationException("The scripted keys are exhausted.");
        var key = ScriptedKeys.Dequeue();
        return new ConsoleKeyInfo((char)0, key, shift: false, alt: false, control: false);
    }

    public void Clear() => Lines.Clear();

    /// <summary>True when one recorded write contains <paramref name="fragment"/>.</summary>
    public bool Contains(string fragment) => Lines.Exists(l => l.Contains(fragment, StringComparison.Ordinal));
}
