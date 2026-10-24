using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Cli;
using Orkeon.Infrastructure.Session;

namespace Orkeon.ConsoleApp.Services;

/// <summary>Runs the configured start script once and stores its output in the session state bag.</summary>
internal sealed partial class ReplStartScriptHook
{
    private readonly IStartScriptRunner _runner;
    private readonly ISessionBufferService _session;
    private readonly string? _scriptPath;
    private readonly ILogger<ReplStartScriptHook> _logger;
    private int _ran;

    public ReplStartScriptHook(
        IStartScriptRunner runner,
        ISessionBufferService session,
        IOptions<CliSessionOptions> options,
        ILogger<ReplStartScriptHook> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _runner = runner;
        _session = session;
        _scriptPath = options.Value.StartScript;
        _logger = logger;
    }

    /// <summary>Runs the script on the first call; later calls do nothing. A failure is logged, never thrown.</summary>
    public async System.Threading.Tasks.Task RunOnceAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _ran, 1) == 1)
            return;

        // A state restored from the previous session must not outlive this start.
        _session.SetState(SessionStateKeys.SessionStartOutput, null);
        if (string.IsNullOrWhiteSpace(_scriptPath))
            return;

        try
        {
            var output = await _runner.RunAsync(_scriptPath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output.Stdout))
                return;

            _session.SetState(
                SessionStateKeys.SessionStartOutput,
                $"[Session start script: {_scriptPath}]\n{output.Stdout.TrimEnd()}\n[Script source]\n{output.Source}\n[End of session start script]");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStartScriptFailed(_logger, ex, _scriptPath);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The session start script {Path} failed; the REPL starts without its output.")]
    private static partial void LogStartScriptFailed(ILogger logger, Exception ex, string path);
}
