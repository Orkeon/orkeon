using Orkeon.Compliance.Vfs;

namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// <see cref="ITerminalProbe"/> over the process console. Every member reads or sets
/// <see cref="System.Console"/> at the time it is called, so a stream swapped by the host
/// (<c>Console.SetOut</c>) is the one used.
/// </summary>
[SuppressConsoleCompliance("CONSOLE-ADAPTER: the one implementation of ITerminalProbe over the real console.")]
public sealed class SystemTerminalProbe : ITerminalProbe
{
    /// <summary>The shared instance: the process has one console.</summary>
    public static SystemTerminalProbe Instance { get; } = new();

    private SystemTerminalProbe() { }

    // Fully qualified throughout: the sibling `Orkeon.Cli.TerminalGui.Console` namespace
    // shadows `System.Console` for unqualified references in this assembly.

    /// <inheritdoc />
    public bool IsInputRedirected => System.Console.IsInputRedirected;

    /// <inheritdoc />
    public bool IsOutputRedirected => System.Console.IsOutputRedirected;

    /// <inheritdoc />
    public bool OutputIsUtf8 => System.Console.OutputEncoding.CodePage is 65001;

    /// <inheritdoc />
    public bool TreatControlCAsInput
    {
        get => System.Console.TreatControlCAsInput;
        set => System.Console.TreatControlCAsInput = value;
    }

    /// <inheritdoc />
    public TextWriter Output => System.Console.Out;

    /// <inheritdoc />
    public TextWriter Diagnostics => System.Console.Error;
}
