using Orkeon.Hosting;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IRunnerConsole"/> over in-memory buffers, handed to a CLI verb in place
/// of the process console: what the verb writes is read back from <see cref="Output"/> and
/// <see cref="ErrorOutput"/>; its input is empty and no interrupt is ever delivered.
/// </summary>
internal sealed class FakeRunnerConsole : IRunnerConsole
{
    /// <summary>The standard output buffer.</summary>
    public StringWriter Output { get; } = new();

    /// <summary>The standard error buffer.</summary>
    public StringWriter ErrorOutput { get; } = new();

    TextWriter IRunnerConsole.Output => Output;

    TextWriter IRunnerConsole.ErrorOutput => ErrorOutput;

    /// <inheritdoc />
    public TextReader Input { get; } = new StringReader(string.Empty);

    /// <inheritdoc />
    public bool IsInputRedirected => true;

    /// <summary>Accepts the handler and never calls it.</summary>
    public IDisposable OnInterrupt(Func<bool> handler) => new Registration();

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
            // Nothing was registered anywhere.
        }
    }
}
