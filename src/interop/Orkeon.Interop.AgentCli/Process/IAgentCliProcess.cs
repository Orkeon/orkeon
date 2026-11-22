using System.Threading.Channels;
using Orkeon.Interop.AgentCli.Engine;

namespace Orkeon.Interop.AgentCli.Process;

/// <summary>A line channel: what the other side wrote, and a way to write to it.</summary>
internal interface IAgentCliLineChannel
{
    /// <summary>The lines read, in order; completed when the other side closes.</summary>
    ChannelReader<string> Incoming { get; }

    /// <summary>Writes one line.</summary>
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}

/// <summary>
/// One running agent CLI: its standard output and input as a line channel, the bridge connection its relay
/// opens, and a way to end it. Disposing it kills the process tree.
/// </summary>
internal interface IAgentCliProcess : IAgentCliLineChannel, IAsyncDisposable
{
    /// <summary>The bridge connection, once the CLI has started the relay and the relay has reached the host.</summary>
    Task<IAgentCliLineChannel> Bridge { get; }

    /// <summary>The last lines of the CLI's standard error, for a failure message.</summary>
    string StandardErrorTail { get; }

    /// <summary>The exit code once the process has ended; null while it runs.</summary>
    int? ExitCode { get; }
}

/// <summary>Starts the processes of the sessions.</summary>
internal interface IAgentCliLauncher
{
    /// <summary>
    /// Starts one CLI for a session: writes <paramref name="systemPrompt"/> where the CLI reads it, opens the
    /// bridge's socket, and starts the process the engine describes.
    /// </summary>
    IAgentCliProcess Start(IAgentCliEngine engine, string systemPrompt, string? model);
}
