
namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Hosts an A2A-compatible HTTP server that exposes local agents for remote task submission.
/// </summary>
public interface IA2AServer : IAsyncDisposable
{
    /// <summary>Starts the A2A server.</summary>
    System.Threading.Tasks.Task StartAsync(CancellationToken ct = default);

    /// <summary>Stops the A2A server gracefully.</summary>
    System.Threading.Tasks.Task StopAsync(CancellationToken ct = default);

    /// <summary>Gets whether the server is currently running.</summary>
    bool IsRunning { get; }
}
