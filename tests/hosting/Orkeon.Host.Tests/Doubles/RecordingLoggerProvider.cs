using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>Hands every category one <see cref="RecordingLogger"/>: what a host logged while it was built.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public RecordingLogger Logger { get; } = new();

    public ILogger CreateLogger(string categoryName) => Logger;

    public void Dispose()
    {
        // Nothing to release; the entries stay readable once the host is disposed.
    }
}
