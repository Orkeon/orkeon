using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Orkeon.Infrastructure.MCP;

/// <summary>
/// MCP transport that communicates with a server process via stdin/stdout.
/// Each JSON-RPC message is sent as a single line.
/// </summary>
public partial class StdioMcpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private SysProcess? _process;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _readLoopTask;
    private CancellationTokenSource? _readCts;
    private volatile bool _isConnected;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <summary>Initializes a new instance of <see cref="StdioMcpTransport"/>.</summary>
    /// <param name="config">The MCP server configuration specifying the process to launch.</param>
    /// <param name="logger">Optional logger.</param>
    public StdioMcpTransport(McpServerConfig config, ILogger<StdioMcpTransport>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _logger = logger ?? NullLogger<StdioMcpTransport>.Instance;
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
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_isConnected) return;

        if (_process == null)
            _process = StartProcess();

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
            throw new InvalidOperationException("Transport is not connected.");

        var id = request.Id ?? throw new ArgumentException("Request must have an Id.", nameof(request));

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
                finally
                {
                    _writeLock.Release();
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Read-loop fault barrier: an unexpected error reading the child process stdout is logged and the loop terminates cleanly, faulting pending requests, rather than crashing the host (cancellation is handled separately).")]
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
                    if (response?.Id != null && _pendingRequests.TryRemove(response.Id.Value, out var tcs))
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
            _isConnected = false;
            // Complete any remaining pending requests with errors
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new InvalidOperationException("Transport disconnected."));
            }
            _pendingRequests.Clear();
        }
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
}
