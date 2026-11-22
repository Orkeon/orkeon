using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Interop.AgentCli.Relay;

/// <summary>
/// The relay between an agent CLI and the Orkeon host that drives it (ADR-020). The CLI starts it as the
/// stdio MCP server of its <c>--mcp-config</c>, with one argument: the path of the Unix domain socket the
/// host listens on. Every line read on standard input goes to the socket, every line read on the socket
/// goes to standard output, unchanged.
/// <para>
/// The first line the host writes is the relay's own, never forwarded: <c>{"orkeon_relay":{"pid":N}}</c>,
/// the process id of the CLI. When the socket closes — the host stopped or was killed while a tool call
/// was pending, which the CLI survives (probe 12) — the relay kills that process tree, itself included,
/// so no CLI outlives its host (probe 27). When standard input closes, the CLI is gone: the relay exits. Either way the relay removes the host's scratch folder (<c>orkeon-agent-cli-*</c>, the socket's parent), so a host that exited without ending its sessions leaves nothing in the temporary directory.
/// </para>
/// </summary>
[SuppressConsoleCompliance("CONSOLE-SINK: the relay's standard input and output are the MCP protocol of the CLI that started it.")]
internal static class Program
{
    /// <summary>The exit code of a relay started without a socket path or unable to reach it.</summary>
    internal const int ExitUsage = 64;

    /// <summary>The prefix of the scratch folder the host creates for one CLI process (<c>AgentCliProcessLauncher</c>).</summary>
    internal const string ScratchPrefix = "orkeon-agent-cli-";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The streams live as long as the process, which exits under its pumps.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The socket outlives the pumps: the process exits before it is disposed.")]
    internal static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            await Console.Error.WriteLineAsync("usage: Orkeon.Interop.AgentCli.Relay <socket path>").ConfigureAwait(false);
            return ExitUsage;
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(args[0])).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            await Console.Error.WriteLineAsync($"the relay could not reach the Orkeon host: {ex.Message}").ConfigureAwait(false);
            return ExitUsage;
        }

        // The streams live as long as the process: a pump may still read when the relay exits, so none is disposed.
        var network = new NetworkStream(socket, ownsSocket: false);
        var fromHost = new StreamReader(network, new UTF8Encoding(false));
        var toHost = new StreamWriter(network, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        var guarded = ReadGuardedPid(await fromHost.ReadLineAsync().ConfigureAwait(false));

        var stdout = Console.OpenStandardOutput();
        var toCli = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var fromCli = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        var hostToCli = PumpAsync(fromHost, toCli);
        var cliToHost = PumpAsync(fromCli, toHost);

        var first = await Task.WhenAny(hostToCli, cliToHost).ConfigureAwait(false);
        if (first == hostToCli)
        {
            // The host is gone: nothing will ever answer the CLI's pending calls, and nothing will
            // remove its scratch folder. The folder goes first: the kill ends the relay too.
            TryDeleteScratch(ScratchFolderOf(args[0]));
            KillTree(guarded);
            Environment.Exit(0);
        }

        // The CLI closed its side: let the host see the end of the stream, then exit. The CLI is
        // gone, so its session is over: the folder goes too, which also covers a host that exited
        // without ending its sessions (its pipes closed, the CLI ended on its own).
        socket.Shutdown(SocketShutdown.Both);
        TryDeleteScratch(ScratchFolderOf(args[0]));
        Environment.Exit(0);
        return 0;
    }

    /// <summary>The CLI's process id from the host's first line; null when the line is not the preamble.</summary>
    internal static int? ReadGuardedPid(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            return JsonNode.Parse(line)?["orkeon_relay"]?["pid"]?.GetValue<int>();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static async Task PumpAsync(StreamReader from, StreamWriter to)
    {
        try
        {
            while (await from.ReadLineAsync().ConfigureAwait(false) is { } line)
                await to.WriteLineAsync(line).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A side closed under the read or the write: the pump is over either way.
        }
        catch (ObjectDisposedException)
        {
            // Same: the stream went away with its side.
        }
    }

    /// <summary>
    /// The scratch folder holding the socket, when it is one the host created
    /// (<c>orkeon-agent-cli-*</c>, the parent of the socket); null for any other path.
    /// </summary>
    internal static string? ScratchFolderOf(string socketPath)
    {
        var folder = Path.GetDirectoryName(socketPath);
        return folder is not null && Path.GetFileName(folder).StartsWith(ScratchPrefix, StringComparison.Ordinal)
            ? folder
            : null;
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: the host's private scratch folder of one CLI process, never a mount of a run, removed when the host is gone.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best effort: a folder left behind in the temporary directory is not a failure of the relay.")]
    private static void TryDeleteScratch(string? folder)
    {
        if (folder is null)
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Left to the temporary directory's cleaner.
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best effort: the CLI may already be gone, and the relay exits whatever happens.")]
    private static void KillTree(int? pid)
    {
        if (pid is not { } id)
            return;
        try
        {
            using var process = Process.GetProcessById(id);
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Gone already, or not ours to kill.
        }
    }
}
