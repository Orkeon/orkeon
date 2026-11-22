using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Orkeon.Compliance.Vfs;
using Orkeon.Interop.AgentCli.Engine;
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Orkeon.Interop.AgentCli.Process;

/// <summary>
/// Starts a real agent CLI (ADR-020). Each session gets a private folder of the temporary directory, mode
/// 0700 outside Windows: the system prompt file, the bridge's Unix domain socket and an empty working folder
/// the CLI runs in. The CLI starts <c>Orkeon.Interop.AgentCli.Relay</c> from the <c>--mcp-config</c>
/// document; the relay reaches the socket, and the host hands it the CLI's process id first, so the relay
/// can kill the CLI should the host go away (probe 27).
/// </summary>
[SuppressVfsCompliance("OUT-OF-SCOPE: the agent CLI is a process of the user's machine; its prompt file, socket and working folder are its own, never a mount of the run.")]
internal sealed partial class AgentCliProcessLauncher : IAgentCliLauncher
{
    /// <summary>The relay's assembly name, next to this one in every host's output.</summary>
    internal const string RelayName = "Orkeon.Interop.AgentCli.Relay";

    private readonly ILogger _logger;
    private readonly string? _relayOverride;

    public AgentCliProcessLauncher(ILogger logger, string? relayPath = null)
    {
        _logger = logger;
        _relayOverride = relayPath;
    }

    public IAgentCliProcess Start(IAgentCliEngine engine, string systemPrompt, string? model)
    {
        var folder = Directory.CreateTempSubdirectory("orkeon-agent-cli-").FullName;
        LiveFolders.Track(folder);
        Socket? listener = null;
        try
        {
            var promptPath = Path.Join(folder, "system-prompt.md");
            File.WriteAllText(promptPath, systemPrompt, new UTF8Encoding(false));
            var workingDirectory = Path.Join(folder, "work");
            Directory.CreateDirectory(workingDirectory);
            var socketPath = Path.Join(folder, "b.sock");

            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(socketPath));
                listener.Listen(1);
            }
            catch
            {
                listener.Dispose();
                throw;
            }

            var (relayCommand, relayArguments) = RelayCommand(socketPath, folder);
            var mcpConfig = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    [ClaudeCodeEngine.BridgeServerName] = new JsonObject
                    {
                        ["command"] = relayCommand,
                        ["args"] = new JsonArray([.. relayArguments.Select(argument => (JsonNode?)argument)]),
                    },
                },
            }.ToJsonString();

            var commandLine = engine.BuildCommandLine(new AgentCliLaunch(promptPath, mcpConfig, model, workingDirectory));
            return RunningAgentCli.Start(commandLine, workingDirectory, listener, folder, _logger);
        }
        catch
        {
            listener?.Dispose();
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>
    /// How the CLI starts the relay: its apphost in a host that carries its own runtime, else <c>dotnet exec</c> on
    /// its assembly — with the runtime configuration beside it, or one written to the session's folder, since a
    /// package's <c>lib/</c> folder carries the assembly alone. Looked for next to the host
    /// (<see cref="AppContext.BaseDirectory"/>), then next to this assembly.
    /// </summary>
    private (string Command, IReadOnlyList<string> Arguments) RelayCommand(string socketPath, string folder)
    {
        if (_relayOverride is { } overridden)
            return overridden.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? DotnetExec(overridden, socketPath, folder)
                : (overridden, [socketPath]);

        var folders = new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(AgentCliProcessLauncher).Assembly.Location) }
            .Where(candidate => !string.IsNullOrEmpty(candidate))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

        // The apphost only in a host that carries its own runtime: it was published for that host's
        // platform. A host on a shared runtime (the dotnet tool, a build from the sources) may carry the
        // apphost of the machine that packed it, so the assembly runs on the runtime this host runs on.
        if (SharedRuntimeHost() is null)
        {
            var apphost = OperatingSystem.IsWindows() ? RelayName + ".exe" : RelayName;
            foreach (var candidateFolder in folders)
            {
                var candidate = Path.Join(candidateFolder, apphost);
                if (File.Exists(candidate))
                    return (candidate, [socketPath]);
            }
        }

        foreach (var candidateFolder in folders)
        {
            var candidate = Path.Join(candidateFolder, RelayName + ".dll");
            if (File.Exists(candidate))
                return DotnetExec(candidate, socketPath, folder);
        }

        throw new InvalidOperationException(
            $"The agent-cli provider found no {RelayName} next to the host ({string.Join(", ", folders)}): the host must reference Orkeon.Interop.AgentCli, which carries it.");
    }

    private static (string Command, IReadOnlyList<string> Arguments) DotnetExec(string assembly, string socketPath, string folder)
    {
        var runtimeConfig = Path.ChangeExtension(assembly, ".runtimeconfig.json");
        if (!File.Exists(runtimeConfig))
        {
            runtimeConfig = Path.Join(folder, RelayName + ".runtimeconfig.json");
            var version = Environment.Version;
            File.WriteAllText(runtimeConfig, new JsonObject
            {
                ["runtimeOptions"] = new JsonObject
                {
                    ["tfm"] = $"net{version.Major}.{version.Minor}",
                    ["rollForward"] = "Major",
                    ["framework"] = new JsonObject
                    {
                        ["name"] = "Microsoft.NETCore.App",
                        ["version"] = $"{version.Major}.{version.Minor}.0",
                    },
                },
            }.ToJsonString(), new UTF8Encoding(false));
        }

        return (DotnetHost(), ["exec", "--runtimeconfig", runtimeConfig, assembly, socketPath]);
    }

    /// <summary>The <c>dotnet</c> muxer: the one running this host when it runs under it, else the PATH's.</summary>
    private static string DotnetHost()
    {
        // OUT-OF-SCOPE: probing the host's own executable and the SDK's environment variable.
        var processPath = Environment.ProcessPath;
        if (processPath is not null
            && string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            return processPath;
        if (SharedRuntimeHost() is { } shared)
            return shared;
        return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath ? hostPath : "dotnet";
    }

    /// <summary>
    /// The <c>dotnet</c> of the shared runtime this host runs on (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>),
    /// or null when the host carries its own runtime (a self-contained publish) or the layout is another.
    /// </summary>
    private static string? SharedRuntimeHost()
    {
        // OUT-OF-SCOPE: probing the layout of the .NET installation this process runs on.
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtime)));
        if (string.IsNullOrEmpty(root)
            || !string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(runtime))), "shared", StringComparison.OrdinalIgnoreCase))
            return null;
        var muxer = Path.Join(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(muxer) ? muxer : null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best effort: a folder left behind in the temporary directory is not a failure of the call.")]
    internal static void TryDelete(string folder)
    {
        LiveFolders.Forget(folder);
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Left to the temporary directory's cleaner.
        }
    }

    /// <summary>
    /// The scratch folders of the CLI processes this host started and has not ended. A host that exits
    /// without ending its sessions (a script run whose agent scope ends on the engine's thread, after the
    /// process has begun to exit) removes them on <see cref="AppDomain.ProcessExit"/>; the CLI ends on its
    /// own when the host's pipes close, and the relay guards the CLI after a kill of the host.
    /// </summary>
    internal static class LiveFolders
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_folders = new(StringComparer.Ordinal);
        private static int s_hooked;

        /// <summary>Gets the folders tracked now.</summary>
        internal static ICollection<string> Current => s_folders.Keys;

        internal static void Track(string folder)
        {
            s_folders[folder] = 0;
            if (Interlocked.Exchange(ref s_hooked, 1) == 0)
                AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveAll();
        }

        internal static void Forget(string folder) => s_folders.TryRemove(folder, out _);

        internal static void RemoveAll()
        {
            foreach (var folder in s_folders.Keys)
                TryDelete(folder);
        }
    }

    /// <summary>One started CLI and its bridge connection.</summary>
    private sealed partial class RunningAgentCli : IAgentCliProcess
    {
        private const int StderrLines = 20;

        private readonly SysProcess _process;
        private readonly Socket _listener;
        private readonly string _folder;
        private readonly ILogger _logger;
        private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Queue<string> _stderr = new();
        private readonly Lock _stderrGate = new();
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<IAgentCliLineChannel> _bridge = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BridgeConnection? _connection;
        private int _disposed;

        private RunningAgentCli(SysProcess process, Socket listener, string folder, ILogger logger)
        {
            _process = process;
            _listener = listener;
            _folder = folder;
            _logger = logger;
        }

        public ChannelReader<string> Incoming => _stdout.Reader;

        public Task<IAgentCliLineChannel> Bridge => _bridge.Task;

        public string StandardErrorTail
        {
            get
            {
                lock (_stderrGate)
                    return string.Join('\n', _stderr);
            }
        }

        public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup on start failure: the original failure is rethrown.")]
        public static RunningAgentCli Start(AgentCliCommandLine commandLine, string workingDirectory, Socket listener, string folder, ILogger logger)
        {
            var startInfo = new SysProcessStartInfo
            {
                // OUT-OF-SCOPE: the CLI's executable is looked up on the PATH, as a shell would.
                FileName = commandLine.FileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in commandLine.Arguments)
                startInfo.ArgumentList.Add(argument);
            foreach (var removed in commandLine.RemovedEnvironment)
                startInfo.Environment.Remove(removed);
            foreach (var (name, value) in commandLine.Environment)
                startInfo.Environment[name] = value;

            var process = new SysProcess { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                listener.Dispose();
                process.Dispose();
                throw new InvalidOperationException(
                    $"The agent CLI '{commandLine.FileName}' could not be started: {ex.Message}. Install it and sign in (claude /login), or set Llm:AgentCli:Command to its full path.", ex);
            }

            var running = new RunningAgentCli(process, listener, folder, logger);
            running.Begin();
            return running;
        }

        private void Begin()
        {
            _ = Task.Run(ReadStdoutAsync);
            _ = Task.Run(ReadStderrAsync);
            _ = Task.Run(AcceptAsync);
        }

        private async Task ReadStdoutAsync()
        {
            try
            {
                while (await _process.StandardOutput.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } line)
                    await _stdout.Writer.WriteAsync(line, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // The process ended or was disposed.
            }
            finally
            {
                _stdout.Writer.TryComplete();
                // The bridge will never come once the CLI is gone.
                _bridge.TrySetException(new InvalidOperationException("The agent CLI ended before its relay reached the host."));
            }
        }

        private async Task ReadStderrAsync()
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } line)
                {
                    lock (_stderrGate)
                    {
                        _stderr.Enqueue(line);
                        while (_stderr.Count > StderrLines)
                            _stderr.Dequeue();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // The process ended or was disposed.
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                var socket = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
                var connection = new BridgeConnection(socket, _stop.Token);
                // The relay's own first line: the process it kills should this host go away.
                await connection.WriteLineAsync(
                    new JsonObject { ["orkeon_relay"] = new JsonObject { ["pid"] = _process.Id } }.ToJsonString(),
                    _stop.Token).ConfigureAwait(false);
                _connection = connection;
                connection.Begin();
                _bridge.TrySetResult(connection);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException or IOException or InvalidOperationException)
            {
                _bridge.TrySetException(new InvalidOperationException("The agent CLI's relay never reached the host.", ex));
            }
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("The agent CLI closed its input: it has ended.", ex);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best effort: every step of the end runs, whatever the one before it did.")]
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }

            await _stop.CancelAsync().ConfigureAwait(false);
            _connection?.Dispose();
            _listener.Dispose();
            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch
            {
                LogNotEnded(_logger, _process.Id);
            }

            _process.Dispose();
            _stop.Dispose();
            _writeGate.Dispose();
            TryDelete(_folder);
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The agent CLI process {ProcessId} did not end within 10 s of being killed.")]
        private static partial void LogNotEnded(ILogger logger, int processId);
    }

    /// <summary>The relay's connection: newline-delimited lines over the socket.</summary>
    private sealed class BridgeConnection : IAgentCliLineChannel, IDisposable
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly StreamReader _reader;
        private readonly CancellationToken _stop;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

        public BridgeConnection(Socket socket, CancellationToken stop)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: true);
            _reader = new StreamReader(_stream, new UTF8Encoding(false));
            _stop = stop;
        }

        public ChannelReader<string> Incoming => _incoming.Reader;

        public void Begin() => _ = Task.Run(ReadAsync);

        private async Task ReadAsync()
        {
            try
            {
                while (await _reader.ReadLineAsync(_stop).ConfigureAwait(false) is { } line)
                    await _incoming.Writer.WriteAsync(line, _stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // The relay or the host closed the connection.
            }
            finally
            {
                _incoming.Writer.TryComplete();
            }
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw new InvalidOperationException("The agent CLI's relay closed the bridge.", ex);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
            _socket.Dispose();
            _writeGate.Dispose();
        }
    }
}
