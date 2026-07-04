using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Sandbox;

/// <summary>
/// <see cref="ICodeSandbox"/> selector that defers the availability probe of the preferred
/// (primary) sandbox to its first use instead of running it at DI resolution time
/// (R10.3 — ORG-012).
/// </summary>
/// <remarks>
/// <para>
/// The previous wiring blocked inside the <c>ICodeSandbox</c> singleton factory on
/// <c>DockerSandbox.IsAvailableAsync</c> (a <c>docker version</c> child process, up to
/// <see cref="Constants.Security.SandboxDefaults.ContainerTimeoutSeconds"/> seconds) while
/// holding the container's singleton-resolution lock, stacking up concurrent resolutions.
/// This decorator keeps the factory pure synchronous wiring and runs the probe lazily and
/// asynchronously at the first <see cref="ExecuteAsync"/> / <see cref="IsAvailableAsync"/>;
/// the outcome is memoized (thread-safe, probed exactly once per instance — the instance is
/// a DI singleton, preserving the previous "cached for the lifetime of the container"
/// semantics).
/// </para>
/// <para>
/// The R2.2 fail-closed semantics are preserved: when the primary sandbox is unavailable,
/// execution on a non-isolating fallback is refused with the exact same message as
/// <see cref="SecureCodeInterpreterTool"/> (see <see cref="SandboxIsolationGate"/>) unless
/// <see cref="SandboxOptions.AllowHostExecution"/> is explicitly opted in.
/// </para>
/// <para>
/// <see cref="Capabilities"/> reports the primary sandbox's capabilities until the probe has
/// completed (optimistic), then the selected sandbox's exact capabilities. This is safe
/// because the fail-closed gate above is enforced at the execution boundary itself.
/// </para>
/// </remarks>
public sealed partial class LazyProbingCodeSandbox : ICodeSandbox
{
    private readonly ICodeSandbox _primary;
    private readonly ICodeSandbox _fallback;
    private readonly SandboxOptions _options;
    private readonly ILogger<LazyProbingCodeSandbox> _logger;
    private readonly Lazy<Task<bool>> _primaryAvailability;

    /// <summary>Initializes a new instance of <see cref="LazyProbingCodeSandbox"/>.</summary>
    /// <param name="primary">The preferred (OS-isolating) sandbox, e.g. <see cref="DockerSandbox"/>.</param>
    /// <param name="fallback">The fallback sandbox used when the primary is unavailable, e.g. <see cref="HostProcessRunner"/>.</param>
    /// <param name="options">Sandbox configuration options (host-execution opt-in).</param>
    /// <param name="logger">The logger.</param>
    public LazyProbingCodeSandbox(
        ICodeSandbox primary,
        ICodeSandbox fallback,
        IOptions<SandboxOptions> options,
        ILogger<LazyProbingCodeSandbox> logger)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _primary = primary;
        _fallback = fallback;
        _options = options.Value;
        _logger = logger;

        // ExecutionAndPublication: the probe factory runs exactly once even under
        // concurrent first calls; every caller awaits the same memoized task.
        _primaryAvailability = new Lazy<Task<bool>>(
            ProbePrimaryAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Before the probe has completed this optimistically reports the primary sandbox's
    /// capabilities; afterwards it reports the selected sandbox's exact capabilities.
    /// The fail-closed gate in <see cref="ExecuteAsync"/> makes the optimistic phase safe.
    /// </remarks>
    public SandboxCapabilities Capabilities =>
        ProbeOutcomeOrNull == false ? _fallback.Capabilities : _primary.Capabilities;

    /// <inheritdoc />
    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool primaryAvailable;
        try
        {
            // WaitAsync detaches the *waiter* from the memoized probe: a cancelled caller
            // stops waiting while the probe itself runs to completion exactly once.
            primaryAvailable = await _primaryAvailability.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            // Same result shape as the inner sandboxes' cancellation path
            // (see DockerSandbox.ExecuteAsync / HostProcessRunner.ExecuteAsync).
            return new SandboxExecutionResult
            {
                Success = false,
                Error = "Execution was cancelled",
                ExitCode = -1,
                Duration = sw.Elapsed,
                TimedOut = true
            };
        }

        if (primaryAvailable)
            return await _primary.ExecuteAsync(request, ct).ConfigureAwait(false);

        var fallbackCaps = _fallback.Capabilities;
        if (!SandboxIsolationGate.IsOsIsolated(fallbackCaps) && !_options.AllowHostExecution)
        {
            sw.Stop();
            // R2.2 fail-closed gate, relocated with the probe (R10.3): refuse to run
            // LLM-generated code on a non-isolating sandbox without an explicit opt-in.
            LogExecutionRefusedNoIsolation(fallbackCaps.SandboxType);
            return new SandboxExecutionResult
            {
                Success = false,
                Error = SandboxIsolationGate.BuildRefusalMessage(fallbackCaps.SandboxType),
                ExitCode = -1,
                Duration = sw.Elapsed
            };
        }

        LogFallingBackToSandbox(fallbackCaps.SandboxType);
        return await _fallback.ExecuteAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            if (await _primaryAvailability.Value.WaitAsync(ct).ConfigureAwait(false))
                return true;
        }
        catch (OperationCanceledException)
        {
            // Fail-closed, mirroring the inner probes: they swallow cancellation
            // and report "not available".
            return false;
        }

        return await _fallback.IsAvailableAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // The wrapped sandboxes are container-owned singletons disposed by the DI
        // container itself; this decorator holds no resources of its own.
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Memoized probe of the primary sandbox. Deliberately detached from any caller's
    /// cancellation token: tying the single memoized probe to the first caller's token
    /// would poison the cached outcome for every later caller. The probe self-terminates
    /// via the inner sandbox's own timeout (see DockerSandbox.IsAvailableAsync).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed probe barrier: any failure probing an arbitrary ICodeSandbox implementation is logged and treated as 'not available' (false).")]
    private async Task<bool> ProbePrimaryAsync()
    {
        bool available;
        try
        {
            available = await _primary.IsAvailableAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-closed: any probe failure means "not available". The concrete probes
            // already swallow their own errors; this guards arbitrary ICodeSandbox impls.
            LogProbeFailed(ex);
            available = false;
        }

        LogProbeCompleted(_primary.Capabilities.SandboxType, available);
        return available;
    }

    /// <summary>
    /// The memoized probe outcome, or <see langword="null"/> while the probe has not
    /// completed yet. Reading <c>Result</c> here is non-blocking: it is guarded by
    /// <see cref="Task.IsCompletedSuccessfully"/>.
    /// </summary>
    private bool? ProbeOutcomeOrNull
    {
        get
        {
            if (!_primaryAvailability.IsValueCreated)
                return null;

            var probe = _primaryAvailability.Value;
            return probe.IsCompletedSuccessfully ? probe.Result : null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sandbox availability probe completed: '{SandboxType}' available={Available}")]
    private partial void LogProbeCompleted(string SandboxType, bool Available);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sandbox availability probe failed; treating the primary sandbox as unavailable (fail-closed)")]
    private partial void LogProbeFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Code execution refused: primary sandbox unavailable and fallback '{SandboxType}' provides no OS isolation (AllowHostExecution=false)")]
    private partial void LogExecutionRefusedNoIsolation(string SandboxType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Primary sandbox unavailable; falling back to sandbox '{SandboxType}'")]
    private partial void LogFallingBackToSandbox(string SandboxType);
}
