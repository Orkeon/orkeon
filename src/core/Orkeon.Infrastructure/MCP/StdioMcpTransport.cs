using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP transport that communicates with a server process via stdin/stdout.
/// Each JSON-RPC message is sent as a single line.
/// </summary>
[Experimental("ORKEXP004", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public partial class StdioMcpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private readonly string _server;
    private SysProcess? _process;
    // Keyed by the raw JSON text of the id: JSON-RPC ids may be numbers or strings.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _readLoopTask;
    private Task? _standardErrorTask;
    private CancellationTokenSource? _readCts;
    private volatile bool _isConnected;
    private volatile string? _lastStandardErrorLine;
    private volatile InvalidOperationException? _disconnection;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <summary>Initializes a new instance of <see cref="StdioMcpTransport"/>.</summary>
    /// <param name="config">The MCP server configuration specifying the process to launch.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="serverId">
    /// The server's id under <c>MCP:Servers</c>, which names its stderr lines in the log; the
    /// command when null.
    /// </param>
    public StdioMcpTransport(McpServerConfig config, ILogger<StdioMcpTransport>? logger = null, string? serverId = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _logger = logger ?? NullLogger<StdioMcpTransport>.Instance;
        _server = serverId ?? config.Command;
    }

    /// <summary>Initializes a new instance of <see cref="StdioMcpTransport"/> for testing with an already-started process.</summary>
    /// <param name="process">An already-started process to communicate with.</param>
    /// <param name="logger">Optional logger.</param>
    public StdioMcpTransport(SysProcess process, ILogger<StdioMcpTransport>? logger = null)
    {
        _config = new McpServerConfig();
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
        _logger = logger ?? NullLogger<StdioMcpTransport>.Instance;
        _server = $"process {process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_isConnected) return;

        if (_process == null)
            _process = StartProcess();

        // The server's stderr is read for as long as it lives (GAP-35). Unread, a server writing
        // more than the pipe holds (64 KiB on Linux) blocked on its next write — its answers with
        // it — and the reason a server gave as it stopped never reached this process.
        if (_process.StartInfo.RedirectStandardError)
            _standardErrorTask = Task.Run(ReadStandardErrorAsync, CancellationToken.None);

        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = Task.Run(() => ReadLoopAsync(_readCts.Token), CancellationToken.None);
        _isConnected = true;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup on startup failure: a failed Kill of a process that may not have started is swallowed so it cannot mask the original start failure (which is rethrown).")]
    private SysProcess StartProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_config.Args != null)
        {
            foreach (var arg in _config.Args)
                startInfo.ArgumentList.Add(arg);
        }

        foreach (var kvp in _config.Env)
            startInfo.Environment[kvp.Key] = kvp.Value;

        var process = new SysProcess { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Ensure no zombie process is left behind on startup failure
            try { process.Kill(entireProcessTree: true); } catch { /* process may not have started */ }
            process.Dispose();
            LogMcpProcessStartFailed(ex, _config.Command);
            throw;
        }

        return process;
    }

    /// <inheritdoc />
    public Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_isConnected || _process == null)
            throw _disconnection ?? new InvalidOperationException("Transport is not connected.");

        var id = request.Id?.GetRawText() ?? throw new ArgumentException("Request must have an Id.", nameof(request));

        return SendRequestAsyncCore();

        async Task<JsonRpcResponse> SendRequestAsyncCore()
        {
            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

            try
            {
                var json = JsonSerializer.Serialize(request);
                await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // The server closed its stdin — it is going, or gone. The read loop reaches the
                    // end of its output and fails this request with why, its last stderr line
                    // included; a broken pipe alone says nothing.
                }
                finally
                {
                    _writeLock.Release();
                }

                // The read loop may have ended after the check at the top, failing the requests it
                // found waiting before this one was among them: this one fails with the same reason
                // rather than waiting for an answer no one will write.
                if (!_isConnected)
                    tcs.TrySetException(_disconnection ?? new InvalidOperationException("Transport disconnected."));

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }
    }

    /// <inheritdoc />
    public Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!_isConnected || _process == null)
            throw _disconnection ?? new InvalidOperationException("Transport is not connected.");

        return SendNotificationCoreAsync();

        async Task SendNotificationCoreAsync()
        {
            var json = JsonSerializer.Serialize(notification);
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Read-loop fault barrier: an unexpected error reading the child process stdout is logged and the loop terminates cleanly, faulting pending requests, rather than crashing the host (cancellation is handled separately).")]
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var endOfOutput = false;
        try
        {
            while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    endOfOutput = true;
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
                    if (response?.Id is { } responseId &&
                        _pendingRequests.TryRemove(responseId.GetRawText(), out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
                catch (JsonException ex)
                {
                    LogFailedToParseResponse(ex, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            LogReadLoopError(ex);
        }
        finally
        {
            // The server went away on its own — its output ended, or it exited — rather than being
            // disconnected by this side: say why, before the requests waiting on it fail with it.
            var serverLeft = endOfOutput || (_process is { HasExited: true } && !ct.IsCancellationRequested);
            var disconnection = serverLeft
                ? await DescribeDisconnectionAsync().ConfigureAwait(false)
                : new InvalidOperationException("Transport disconnected.");
            _disconnection = disconnection;
            _isConnected = false;
            // A request registers itself, then reads _isConnected; this side writes _isConnected,
            // then reads the registered requests. The fence keeps the two from missing each other.
            Interlocked.MemoryBarrier();
            // Complete any remaining pending requests with errors
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(disconnection);
            }
            _pendingRequests.Clear();
        }
    }

    /// <summary>
    /// Reads the server's stderr until it closes: each line goes to the log at Debug, under the
    /// server's id, and the last one is kept for <see cref="DescribeDisconnectionAsync"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort diagnostics: a failure reading the server's stderr must never take the transport down; it only ends the reading.")]
    private async Task ReadStandardErrorAsync()
    {
        try
        {
            while (await _process!.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                _lastStandardErrorLine = line;
                LogServerStandardError(_server, line);
            }
        }
        catch (Exception)
        {
            // The process was disposed under the reader, or its stream failed: the reading ends.
        }
    }

    /// <summary>
    /// Why the server is gone: "Transport disconnected." alone left the operator guessing (GAP-35).
    /// The exit code once the process has exited, and the last line it wrote on stderr — where a
    /// server says why it refuses, and where <c>orkeon mcp serve</c> writes its refusal. Waits a
    /// moment for both: the end of a server's output comes just before its exit, and its stderr
    /// may still hold the line that says why.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort diagnostics: the disconnection is reported whatever the process API answers while it is being torn down.")]
    private async Task<InvalidOperationException> DescribeDisconnectionAsync()
    {
        int? exitCode = null;
        try
        {
            using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _process!.WaitForExitAsync(exit.Token).ConfigureAwait(false);
            exitCode = _process.ExitCode;
            if (_standardErrorTask is not null)
                await _standardErrorTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Still running, or already disposed: what is known is said.
        }

        var reason = exitCode is { } code
            ? $"Transport disconnected: the MCP server exited with code {code.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "Transport disconnected: the MCP server closed its output";
        return new InvalidOperationException(_lastStandardErrorLine is { } line
            ? $"{reason}; its last line on stderr: {line}"
            : $"{reason}.");
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort teardown: failures awaiting the read loop or killing the child process are logged and swallowed so disposal always completes.")]
    public async ValueTask DisposeAsync()
    {
        _isConnected = false;
        if (_readCts is not null)
            await _readCts.CancelAsync().ConfigureAwait(false);

        if (_readLoopTask != null)
        {
            try { await _readLoopTask.ConfigureAwait(false); }
            catch (Exception ex) { LogMcpCleanupError(ex, "read loop"); }
        }

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.StandardInput.Close();
                using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                LogMcpCleanupError(ex, "process shutdown");
            }
        }

        if (_standardErrorTask is not null)
        {
            // The process is gone, so its stderr closes; a grandchild that inherited the pipe may
            // keep it open, and is not waited for.
            try { await _standardErrorTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { /* left to end on its own */ }
        }

        _process?.Dispose();
        _readCts?.Dispose();
        _writeLock.Dispose();

        GC.SuppressFinalize(this);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to parse JSON-RPC response: {Line}")]
    private partial void LogFailedToParseResponse(Exception ex, string line);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Error in stdio read loop")]
    private partial void LogReadLoopError(Exception ex);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Failed to start MCP server process: {Command}")]
    private partial void LogMcpProcessStartFailed(Exception ex, string command);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "MCP cleanup error during {Phase}")]
    private partial void LogMcpCleanupError(Exception ex, string phase);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "MCP server '{Server}' stderr: {Line}")]
    private partial void LogServerStandardError(string server, string line);
}
