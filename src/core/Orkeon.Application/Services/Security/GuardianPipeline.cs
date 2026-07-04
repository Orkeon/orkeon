
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Security;

namespace Orkeon.Application.Services.Security;

/// <summary>
/// Pipeline that orchestrates multiple guardians by phase, evaluating each context
/// against registered guards and aggregating results.
/// </summary>
public partial class GuardianPipeline
{
    private readonly Dictionary<GuardPhase, List<IGuardian>> _guards = [];
    private readonly GuardianPolicyEngine _policyEngine;
    private readonly ILogger<GuardianPipeline> _logger;
    private readonly IAuditLogger? _auditLogger;
    private readonly IAgentLifecycleManager? _lifecycleManager;

    /// <summary>
    /// When true, automatically kills an agent if a Block result contains High or Critical severity violations.
    /// Requires IAgentLifecycleManager and a non-empty AgentId in GuardContext.
    /// </summary>
    public bool AutoKillOnCritical { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="GuardianPipeline"/>.
    /// </summary>
    public GuardianPipeline(
        GuardianPolicyEngine policyEngine,
        ILogger<GuardianPipeline> logger,
        IAuditLogger? auditLogger = null,
        IAgentLifecycleManager? lifecycleManager = null)
    {
        ArgumentNullException.ThrowIfNull(policyEngine);
        _policyEngine = policyEngine;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _auditLogger = auditLogger;
        _lifecycleManager = lifecycleManager;
    }

    /// <summary>
    /// Registers a guardian for the specified phase.
    /// </summary>
    public void AddGuard(GuardPhase phase, IGuardian guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (!_guards.TryGetValue(phase, out var guardList))
        {
            guardList = [];
            _guards[phase] = guardList;
        }
        guardList.Add(guard);
    }

    /// <summary>
    /// Executes all registered guardians for the context's phase.
    /// Returns Block immediately if any guard blocks; aggregates warnings otherwise.
    /// </summary>
    public System.Threading.Tasks.Task<GuardResult> ExecuteAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync(context, ct);
    }

    private async System.Threading.Tasks.Task<GuardResult> ExecuteCoreAsync(GuardContext context, CancellationToken ct)
    {
        var policy = _policyEngine.GetPolicy(context.CrewId, context.AgentId);

        // If this phase is disabled in policy, allow immediately
        if (!policy.IsGuardPhaseEnabled(context.Phase))
        {
            LogGuardPhaseDisabled(context.Phase, context.CrewId, context.AgentId);
            return GuardResult.Allow();
        }

        // If no guards registered for this phase, allow
        if (!_guards.TryGetValue(context.Phase, out var guards) || guards.Count == 0)
            return GuardResult.Allow();

        var allViolations = new List<GuardViolation>();

        foreach (var guard in guards)
        {
            ct.ThrowIfCancellationRequested();
            var (earlyReturn, result) = await EvaluateGuardAsync(guard, context, ct).ConfigureAwait(false);
            if (earlyReturn)
                return result!;
            if (result?.Action == GuardAction.Warn && result.Violations.Count > 0)
                allViolations.AddRange(result.Violations);
        }

        if (allViolations.Count > 0)
        {
            var warnResult = GuardResult.Warn(
                $"{allViolations.Count} warning(s) detected during {context.Phase} phase",
                allViolations);
            await LogAuditEventAsync(context, warnResult, ct).ConfigureAwait(false);
            return warnResult;
        }

        return GuardResult.Allow();
    }

    /// <summary>
    /// Evaluates a single guardian. Returns (true, result) if the pipeline should stop immediately.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-safe security barrier: a guardian throwing (cancellation re-thrown) is treated as a Block to avoid bypassing security, never propagated.")]
    private async System.Threading.Tasks.Task<(bool EarlyReturn, GuardResult? Result)> EvaluateGuardAsync(
        IGuardian guard, GuardContext context, CancellationToken ct)
    {
        try
        {
            var result = await guard.CheckAsync(context, ct).ConfigureAwait(false);
            if (result.Action == GuardAction.Block)
            {
                LogGuardianBlocked(context.Phase, result.Reason, result.Violations.Count);
                await LogAuditEventAsync(context, result, ct).ConfigureAwait(false);
                TryAutoKill(context, result);
                return (true, result);
            }
            return (false, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogGuardianException(ex, guard.GetType().Name, context.Phase);
            // Fail-safe: treat exceptions as block to avoid bypassing security
            var violation = new GuardViolation(
                guard.GetType().Name,
                context.Phase,
                $"Guard threw exception: {ex.Message}",
                GuardThreatSeverity.High,
                DateTime.UtcNow);
            var blockResult = GuardResult.Block($"Guard error: {ex.Message}", [violation]);
            await LogAuditEventAsync(context, blockResult, ct).ConfigureAwait(false);
            TryAutoKill(context, blockResult);
            return (true, blockResult);
        }
    }

    private void TryAutoKill(GuardContext context, GuardResult result)
    {
        if (!AutoKillOnCritical
            || _lifecycleManager is null
            || string.IsNullOrEmpty(context.AgentId))
            return;

        bool hasCriticalViolation = result.Violations.Any(v =>
            v.Severity is GuardThreatSeverity.High or GuardThreatSeverity.Critical);

        if (!hasCriticalViolation)
            return;

        if (!Guid.TryParse(context.AgentId, out var guid))
            return;

        var agentId = AgentId.From(guid);
        if (!_lifecycleManager.IsRegistered(agentId))
        {
            LogAutoKillAgentNotRegistered(context.AgentId);
            return;
        }

        var reason = $"Guardian auto-kill: {result.Reason}";
        LogAutoKillTriggered(context.AgentId, reason);
        _lifecycleManager.Kill(agentId, reason);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort audit logging: an audit-sink failure is logged and swallowed so it cannot mask or block the guardian decision being audited.")]
    private async System.Threading.Tasks.Task LogAuditEventAsync(GuardContext context, GuardResult result, CancellationToken ct)
    {
        if (_auditLogger is null)
            return;

        try
        {
            var severity = result.Action switch
            {
                GuardAction.Block => AuditSeverity.Warning,
                GuardAction.Warn => AuditSeverity.Info,
                _ => AuditSeverity.Debug
            };

            var auditEvent = AuditEventBuilders.SecurityEvent(
                correlationId: Guid.NewGuid().ToString("N"),
                threatType: $"Guardian:{context.Phase}",
                description: result.Reason ?? "Guardian check completed",
                severity: severity,
                agentRole: context.AgentId);

            await _auditLogger.LogAsync(auditEvent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditEventFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Guardian phase {Phase} is disabled by policy for crew={CrewId} agent={AgentId}")]
    private partial void LogGuardPhaseDisabled(GuardPhase phase, string crewId, string agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Guardian blocked operation: phase={Phase}, reason={Reason}, violations={Count}")]
    private partial void LogGuardianBlocked(GuardPhase phase, string? reason, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Guardian {GuardType} threw an exception during phase {Phase}")]
    private partial void LogGuardianException(Exception ex, string guardType, GuardPhase phase);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AutoKill: agent {AgentId} not registered in lifecycle manager; skipping kill")]
    private partial void LogAutoKillAgentNotRegistered(string agentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AutoKill triggered for agent {AgentId}. Reason: {Reason}")]
    private partial void LogAutoKillTriggered(string agentId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to log guardian audit event")]
    private partial void LogAuditEventFailed(Exception ex);
}
