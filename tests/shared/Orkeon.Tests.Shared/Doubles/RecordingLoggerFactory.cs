using Microsoft.Extensions.Logging;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// <see cref="ILoggerFactory"/> that always returns the same shared
/// <see cref="RecordingLogger"/>. Replaces <c>Mock&lt;ILoggerFactory&gt;</c>.
/// </summary>
public sealed class RecordingLoggerFactory : ILoggerFactory
{
    /// <summary>Shared logger receiving every captured entry.</summary>
    public RecordingLogger Logger { get; } = new();

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) { }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => Logger;

    /// <inheritdoc />
    public void Dispose() { }
}
