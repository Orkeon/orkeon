using System.Threading.Channels;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// An <see cref="IRunnerConsole"/> whose stdin is fed line by line while the verb runs, and ends when <see cref="CompleteInput"/>
/// is called: what lets a test send a request, wait for the server to be inside it, then cancel it.
/// </summary>
internal sealed class FakeGatedStdinConsole : IRunnerConsole, IDisposable
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly GatedReader _in = new();

    public string Stdout => _out.ToString();

    public string Stderr => _error.ToString();

    public TextWriter Output => _out;

    public TextWriter ErrorOutput => _error;

    public TextReader Input => _in;

    public bool IsInputRedirected => true;

    /// <summary>Sends one line to the verb's stdin.</summary>
    public void Send(string line) => _in.Lines.Writer.TryWrite(line);

    /// <summary>Ends the verb's stdin.</summary>
    public void CompleteInput() => _in.Lines.Writer.TryComplete();

    public IDisposable OnInterrupt(Func<bool> handler) => new Registration();

    public void Dispose()
    {
        _out.Dispose();
        _error.Dispose();
        _in.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class GatedReader : TextReader
    {
        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await Lines.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
    }
}
