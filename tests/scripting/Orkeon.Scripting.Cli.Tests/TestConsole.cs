namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Captures <see cref="Console.Out"/> and <see cref="Console.Error"/> for the duration of a
/// scope. <see cref="Console"/> is process-global, so callers must hold <see cref="Gate"/>
/// (see <see cref="CliCollection"/>) to avoid interleaving with sibling tests.
/// </summary>
internal sealed class TestConsole : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public TestConsole()
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(_out);
        Console.SetError(_error);
    }

    public string Stdout => _out.ToString();
    public string Stderr => _error.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        _out.Dispose();
        _error.Dispose();
    }
}
