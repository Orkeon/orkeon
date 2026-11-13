using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// Records what is written and, at the first write, the value <see cref="Probe"/> reads: the state the host sees
/// when the first line of a question reaches the console.
/// </summary>
internal sealed class FakePromptProbeConsole : IConsoleAdapter
{
    private bool _probed;

    /// <summary>Read at the first write; set by the test.</summary>
    public Func<string?>? Probe { get; set; }

    /// <summary>What <see cref="Probe"/> returned at the first write.</summary>
    public string? AtFirstWrite { get; private set; }

    public List<string> Lines { get; } = [];

    public void Write(string text) => Record(text);

    public void WriteLine(string text) => Record(text);

    public string? ReadLine() => null;

    public ConsoleKeyInfo ReadKey(bool intercept = false) => default;

    public void Clear() => Lines.Clear();

    private void Record(string text)
    {
        if (!_probed)
        {
            _probed = true;
            AtFirstWrite = Probe?.Invoke();
        }

        Lines.Add(text);
    }
}
