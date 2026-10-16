using Microsoft.Extensions.Logging;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>
/// An <see cref="ILoggerProvider"/> handing every category one <see cref="CapturingLogger"/>, so a
/// test can read what the REPL's host logged at its start — the runners' tests pass their own provider
/// to <c>RunnerHost.Build</c> the same way.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>The logger every category writes to.</summary>
    public CapturingLogger Logger { get; } = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => Logger;

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: the entries stay readable after the host is disposed.
    }
}
