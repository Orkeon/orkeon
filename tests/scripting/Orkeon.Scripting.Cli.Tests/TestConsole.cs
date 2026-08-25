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
    private readonly TextReader? _originalIn;
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly StringReader? _in;

    /// <summary>
    /// <paramref name="stdin"/>, when given, becomes <see cref="Console.In"/> for the
    /// scope — how a test scripts the inbound half of the <c>--events</c> protocol.
    /// </summary>
    public TestConsole(string? stdin = null)
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(_out);
        Console.SetError(_error);

        if (stdin is not null)
        {
            _originalIn = Console.In;
            _in = new StringReader(stdin);
            Console.SetIn(_in);
        }
    }

    public string Stdout => _out.ToString();
    public string Stderr => _error.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        if (_originalIn is not null)
            Console.SetIn(_originalIn);
        _out.Dispose();
        _error.Dispose();
        _in?.Dispose();
    }
}
