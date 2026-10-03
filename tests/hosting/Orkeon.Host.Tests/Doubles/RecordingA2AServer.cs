using Orkeon.Application.Interfaces.AgentCommunication;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// An A2A server that listens on nothing: it records whether it was started and stopped, and
/// can refuse its start the way the real one refuses a security configuration it cannot honour.
/// </summary>
internal sealed class RecordingA2AServer : IA2AServer
{
    /// <summary>When set, <see cref="StartAsync"/> throws it instead of starting.</summary>
    public Exception? StartFailure { get; init; }

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public bool IsRunning => Started && !Stopped;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (StartFailure is not null)
            throw StartFailure;

        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
