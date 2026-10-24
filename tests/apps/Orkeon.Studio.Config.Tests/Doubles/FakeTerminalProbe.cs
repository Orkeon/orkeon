using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Studio.Config.Tests.Doubles;

/// <summary>Hand-written <see cref="ITerminalProbe"/>: plain fields to configure and inspect.</summary>
internal sealed class FakeTerminalProbe : ITerminalProbe
{
    public bool InputRedirected { get; set; }
    public bool OutputRedirected { get; set; }
    public bool ControlCAsInput;
    public TextWriter Out = TextWriter.Null;
    public TextWriter Err = TextWriter.Null;

    public bool IsInputRedirected => InputRedirected;

    public bool IsOutputRedirected => OutputRedirected;

    public bool OutputIsUtf8 => true;

    public bool TreatControlCAsInput
    {
        get => ControlCAsInput;
        set => ControlCAsInput = value;
    }

    public TextWriter Output => Out;

    public TextWriter Diagnostics => Err;
}
