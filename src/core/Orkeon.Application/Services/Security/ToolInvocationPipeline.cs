using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Telemetry;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Domain.Security;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Services.Security;

/// <summary>
/// Default <see cref="IToolInvocationPipeline"/>: guardian phase → call → truncation →
/// result sanitizer → audit. Every dependency is optional: without a guardian the call is not
/// screened, without a sanitizer the result is not tagged, without an audit logger nothing is
/// recorded — <see cref="Unguarded"/> is that bare pipeline, for an orchestrator built outside
/// dependency injection. The hosts get the full one from <c>AddOrkeonInfrastructure()</c>.
/// </summary>
public sealed partial class ToolInvocationPipeline : IToolInvocationPipeline
{
    /// <summary>The tool a coworker is handed work through; checked by the delegation phase.</summary>
    public const string DelegateWorkToolName = Orkeon.Domain.Tools.CoworkerDelegation.DelegateWorkToolName;

    /// <summary>The tool a coworker is asked a question through; checked by the delegation phase.</summary>
    public const string AskQuestionToolName = Orkeon.Domain.Tools.CoworkerDelegation.AskQuestionToolName;

    /// <summary>
    /// The roles of the agents that delegated synchronously, outermost first: a delegated
    /// task runs inside its delegator's tool call, so its own calls see the chain grow.
    /// </summary>
    private static readonly AsyncLocal<ImmutableList<string>?> s_delegationChain = new();

    // The strictest permission mode among the agents of the synchronous delegation chain: a coworker never runs
    // under a looser mode than the agent that handed it the work.
    private static readonly AsyncLocal<Orkeon.Domain.Security.PermissionMode?> s_delegatedMode = new();

    private readonly IPermissionGate? _permissionGate;
    private readonly IToolApprovalChannel? _approvalChannel;
    private readonly Orkeon.Domain.Security.PermissionMode _defaultMode;
    private readonly IGuardianPipeline? _guardian;
    private readonly IToolResultSanitizer? _sanitizer;
    private readonly IAuditLogger? _auditLogger;
    private readonly ILogger _logger;
    private readonly Orkeon.Domain.Tools.ToolResultLimitsOptions? _limits;
    private readonly Orkeon.Application.Interfaces.Services.IRunScope? _runScope;

    /// <summary>A pipeline with no guardian, no sanitizer and no audit: the call and the truncation only.</summary>
    public static ToolInvocationPipeline Unguarded { get; } = new();

    /// <summary>Initializes a new instance of <see cref="ToolInvocationPipeline"/>.</summary>
    /// <param name="guardian">The guardian pipeline running the tool and delegation phases.</param>
    /// <param name="sanitizer">The sanitizer applied to every successful result.</param>
    /// <param name="auditLogger">The audit trail a <c>ToolExecution</c> event is written to per call.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="limits">The per-tool result cuts; without it the <c>AgentDefaults</c> ones apply.</param>
    /// <param name="permissionGate">The permission gate consulted after the guardian for the calls of an agent; without it nothing is gated.</param>
    /// <param name="options">The approval channel, the default mode and the ambient run scope; without it none is set.</param>
    public ToolInvocationPipeline(
        IGuardianPipeline? guardian = null,
        IToolResultSanitizer? sanitizer = null,
        IAuditLogger? auditLogger = null,
        ILogger<ToolInvocationPipeline>? logger = null,
        Microsoft.Extensions.Options.IOptions<Orkeon.Domain.Tools.ToolResultLimitsOptions>? limits = null,
        IPermissionGate? permissionGate = null,
        ToolInvocationPipelineOptions? options = null)
    {
        _runScope = options?.RunScope;
        _permissionGate = permissionGate;
        _approvalChannel = options?.ApprovalChannel;
        _defaultMode = options?.DefaultMode ?? Orkeon.Domain.Security.PermissionMode.Default;
        _guardian = guardian;
        _sanitizer = sanitizer;
        _auditLogger = auditLogger;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _limits = limits?.Value;
    }

    /// <inheritdoc />
    public bool GatesPermissions => _permissionGate is not null;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return InvokeCoreAsync(invocation, cancellationToken);
    }

    private async System.Threading.Tasks.Task<ToolInvocationResult> InvokeCoreAsync(
        ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var tool = invocation.Tool;
        var caller = WithAmbientCrew(invocation.Caller);
        // A delegation is told from the tool's type, never from its name alone: an MCP server, a plugin or a
        // script can name a tool delegate_work_to_coworker, and such a tool is screened and gated like any other.
        var delegation = Orkeon.Domain.Tools.CoworkerDelegation.Find(tool);
        var isDelegation = delegation is not null;
        var chain = s_delegationChain.Value ?? ImmutableList<string>.Empty;

        var refused = await RefuseAsync(invocation, caller, delegation, chain, cancellationToken).ConfigureAwait(false);
        if (refused is not null)
            return refused;

        // Admitted: the caller charges what a call costs it (a script's tool-call budget) only now, so a refused
        // call costs nothing. What it throws ends the call before the tool runs.
        caller.Admitted?.Invoke();

        var stopwatch = Stopwatch.StartNew();
        var response = await CallToolAsync(invocation, caller, isDelegation, chain, stopwatch, cancellationToken).ConfigureAwait(false);

        // A tool swallows what it throws into a failed answer. One that left the run waiting for a human (FWK-23) is told
        // from the run: the agent's loop ends here with the suspension, it is not handed an error to reason about.
        if (_runScope?.Current?.Suspension is { } suspended)
            throw new Orkeon.Application.Interfaces.Services.RunSuspendedException(suspended.RequestId);

        OrkeonMeter.RecordToolExecution(
            tool.Name, response.Success ? "success" : "failure", stopwatch.Elapsed.TotalSeconds);

        var rawText = response.Success
            ? ToolCallFormatting.FormatResult(response.Result)
            : $"Error: {response.Error}";

        // The one truncation rule (ToolResultLimitsOptions.ResolveFor, defaulting to AgentDefaults), applied before the
        // sanitizer so that what is screened is exactly what the model reads.
        var conversationText = response.AlreadyBounded
            ? rawText
            : ConversationPolicy.TruncateToolResult(
                rawText, _limits?.ResolveFor(tool.Name) ?? AgentDefaults.ResolveMaxToolResultLength(tool.Name));

        if (response.Success && _sanitizer is not null)
        {
            var sanitized = _sanitizer.Sanitize(tool.Name, conversationText, caller.AgentRole);
            conversationText = sanitized.Text;
            if (sanitized.Threats.Count > 0)
                await AuditResultThreatsAsync(tool.Name, caller, sanitized, cancellationToken).ConfigureAwait(false);
        }

        await AuditCallAsync(
            tool.Name, caller, response.Success ? AuditOutcome.Success : AuditOutcome.Failure,
            stopwatch.ElapsedMilliseconds, cancellationToken).ConfigureAwait(false);

        return new ToolInvocationResult
        {
            ConversationText = conversationText,
            RawText = rawText,
            Success = response.Success,
            Response = response,
        };
    }

    // The guardian phase, then the permission gate: the blocked result of the first that refuses, else null.
    private async System.Threading.Tasks.Task<ToolInvocationResult?> RefuseAsync(
        ToolInvocation invocation,
        ToolInvocationCaller caller,
        Orkeon.Domain.Tools.ICoworkerDelegationTool? delegation,
        ImmutableList<string> chain,
        CancellationToken cancellationToken)
    {
        var tool = invocation.Tool;
        var isDelegation = delegation is not null;
        string text;
        var verdict = await GuardAsync(invocation, caller, delegation, chain, cancellationToken).ConfigureAwait(false);
        if (verdict is { IsAllowed: false })
        {
            text = $"Error: Blocked by Guardian ({(isDelegation ? GuardPhase.Delegation : GuardPhase.ToolExecution)}): {verdict.Reason}";
        }
        else
        {
            var refusal = await GateAsync(invocation, caller, isDelegation, cancellationToken).ConfigureAwait(false);
            if (refusal is null)
                return null;
            text = $"Error: Blocked by the permission gate: {refusal}";
        }

        OrkeonMeter.RecordToolExecution(tool.Name, "blocked", null);
        await AuditCallAsync(tool.Name, caller, AuditOutcome.Blocked, 0, cancellationToken).ConfigureAwait(false);
        return new ToolInvocationResult { ConversationText = text, RawText = text, Blocked = true };
    }

    private async System.Threading.Tasks.Task<ToolCallResponse> CallToolAsync(
        ToolInvocation invocation,
        ToolInvocationCaller caller,
        bool isDelegation,
        ImmutableList<string> chain,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var tool = invocation.Tool;
        var inheritedMode = s_delegatedMode.Value;
        try
        {
            // A synchronous delegation runs the coworker's task inside this call: its own
            // tool calls see this agent at the end of the chain.
            if (isDelegation)
            {
                s_delegationChain.Value = chain.Add(caller.AgentRole);
                if (!caller.GateExempt)
                    s_delegatedMode.Value = Stricter(s_delegatedMode.Value, ResolveMode(caller));
            }

            return await tool.CallAsync(new ToolCallRequest(tool.Name, invocation.Arguments), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            OrkeonMeter.RecordToolExecution(tool.Name, "cancelled", stopwatch.Elapsed.TotalSeconds);
            throw;
        }
        catch (Exception)
        {
            OrkeonMeter.RecordToolExecution(tool.Name, "failure", stopwatch.Elapsed.TotalSeconds);
            await AuditCallAsync(tool.Name, caller, AuditOutcome.Failure, stopwatch.ElapsedMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (isDelegation)
            {
                s_delegationChain.Value = chain;
                s_delegatedMode.Value = inheritedMode;
            }
        }
    }

    private async System.Threading.Tasks.Task<GuardResult?> GuardAsync(
        ToolInvocation invocation,
        ToolInvocationCaller caller,
        Orkeon.Domain.Tools.ICoworkerDelegationTool? delegation,
        ImmutableList<string> chain,
        CancellationToken cancellationToken)
    {
        if (_guardian is null)
            return null;

        var isDelegation = delegation is not null;
        var context = new GuardContext
        {
            Phase = isDelegation ? GuardPhase.Delegation : GuardPhase.ToolExecution,
            AgentId = caller.AgentId,
            AgentRole = caller.AgentRole,
            CrewId = caller.CrewId ?? string.Empty,
            ToolName = invocation.Tool.Name,
            ToolArgs = invocation.Arguments,
            DelegationChain = chain,
            TargetAgentRole = delegation?.CoworkerRoleOf(invocation.Arguments),
        };

        return await _guardian.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the call of an agent to the permission gate, after the guardian. Null when the call may run;
    /// else the reason it is refused. An <c>Ask</c> goes to the approval channel when there is one, and is a
    /// refusal without (fail closed). Not gated: no gate, an exempt caller (<c>orkeon mcp serve</c>, the
    /// forge) and the delegation tools (told from their type, <see cref="Orkeon.Domain.Tools.CoworkerDelegation"/>), which hand work to another agent whose own calls are gated under the stricter of its mode and the delegator's.
    /// </summary>
    private async System.Threading.Tasks.Task<string?> GateAsync(
        ToolInvocation invocation, ToolInvocationCaller caller, bool isDelegation, CancellationToken cancellationToken)
    {
        if (_permissionGate is null || caller.GateExempt || isDelegation)
            return null;

        var resolved = Stricter(s_delegatedMode.Value, ResolveMode(caller));
        var tool = invocation.Tool;

        // The wait for the gate and for an operator is not the caller's time (a script stops its clocks).
        using var waiting = caller.WaitScope?.Invoke();

        // A call that must be approved (a script's onToolCall answered "ask") is put to the operator as a
        // default-mode edit; it never relaxes a stricter mode: under plan or dontAsk the call is first put to the
        // gate in that mode, which refuses a write before any operator is asked.
        if (caller.RequireApproval && IsStricterThanDefault(resolved))
        {
            var strictMode = Orkeon.Domain.Security.PermissionModes.ToWireName(resolved);
            var strict = await _permissionGate.CheckAsync(
                tool.Name, invocation.Arguments, strictMode, tool.AccessFor(invocation.Arguments), cancellationToken).ConfigureAwait(false);
            if (strict.Action != PermissionAction.Allow)
                return strict.Message ?? $"tool '{tool.Name}' is not permitted in mode '{strictMode}'.";
        }

        var mode = Orkeon.Domain.Security.PermissionModes.ToWireName(
            caller.RequireApproval ? Orkeon.Domain.Security.PermissionMode.Default : resolved);
        var access = caller.RequireApproval ? Orkeon.Domain.Tools.ToolAccess.Edit : tool.AccessFor(invocation.Arguments);
        var verdict = await _permissionGate.CheckAsync(
            tool.Name, invocation.Arguments, mode, access, cancellationToken).ConfigureAwait(false);
        switch (verdict.Action)
        {
            case PermissionAction.Allow:
                return null;
            case PermissionAction.Ask when _approvalChannel is not null:
                var decision = await _approvalChannel.RequestAsync(
                    new ToolApprovalRequest
                    {
                        ToolName = tool.Name,
                        Arguments = invocation.Arguments.ToImmutableDictionary(),
                        Mode = mode,
                        Reason = verdict.Message ?? $"tool '{tool.Name}' needs an approval in mode '{mode}'.",
                    },
                    cancellationToken).ConfigureAwait(false);
                return decision == ToolApprovalDecision.Deny ? $"the operator refused tool '{tool.Name}'." : null;
            case PermissionAction.Ask:
                return $"{verdict.Message ?? $"tool '{tool.Name}' needs an approval in mode '{mode}'."} No approval channel is available, so the call is denied.";
            default:
                return verdict.Message ?? $"tool '{tool.Name}' is not permitted in mode '{mode}'.";
        }
    }

    private Orkeon.Domain.Security.PermissionMode ResolveMode(ToolInvocationCaller caller)
    {
        if (string.IsNullOrWhiteSpace(caller.PermissionMode))
            return _defaultMode;

        // An unknown name is never read as a looser mode.
        return Orkeon.Domain.Security.PermissionModes.TryParse(caller.PermissionMode, out var parsed)
            ? parsed
            : Orkeon.Domain.Security.PermissionMode.Default;
    }

    // Plan and DontAsk deny everything but reads; Default asks; AcceptEdits and BypassPermissions let more through.
    private static bool IsStricterThanDefault(Orkeon.Domain.Security.PermissionMode mode) =>
        Orkeon.Domain.Security.PermissionMode.Default.Stricter(mode) != Orkeon.Domain.Security.PermissionMode.Default;

    private static Orkeon.Domain.Security.PermissionMode Stricter(
        Orkeon.Domain.Security.PermissionMode? inherited, Orkeon.Domain.Security.PermissionMode own) => own.Stricter(inherited);

    private static ToolInvocationCaller WithAmbientCrew(ToolInvocationCaller caller)
    {
        if (!string.IsNullOrEmpty(caller.CrewId))
            return caller;

        var ambient = LlmUsageScope.Current.CrewId;
        return string.IsNullOrEmpty(ambient) ? caller : caller with { CrewId = ambient };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort audit: an audit-sink failure is logged and swallowed so it can never fail or alter the tool call it records.")]
    private async System.Threading.Tasks.Task AuditCallAsync(
        string toolName, ToolInvocationCaller caller, AuditOutcome outcome, long durationMs, CancellationToken cancellationToken)
    {
        if (_auditLogger is null)
            return;

        try
        {
            // Never the arguments: they are the model's, and may carry what a log must not.
            var auditEvent = AuditEventBuilders.ToolExecution(
                correlationId: Guid.NewGuid().ToString("N"),
                toolName: toolName,
                agentRole: caller.AgentRole,
                outcome: outcome,
                durationMs: durationMs) with
            {
                CrewId = caller.CrewId,
                TaskId = caller.TaskId,
            };
            await _auditLogger.LogAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditFailed(ex, toolName);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort audit: an audit-sink failure is logged and swallowed so it can never fail or alter the tool call it records.")]
    private async System.Threading.Tasks.Task AuditResultThreatsAsync(
        string toolName, ToolInvocationCaller caller, ToolResultSanitization sanitized, CancellationToken cancellationToken)
    {
        OrkeonMeter.RecordSecurityEvent("injection", toolName);
        LogResultThreats(toolName, caller.AgentRole, sanitized.Threats.Count, sanitized.Blocked ? "withheld" : "tagged as data");
        if (_auditLogger is null)
            return;

        try
        {
            var patterns = string.Join(", ", sanitized.Threats.Select(t => $"{t.Pattern} ({t.Severity})").Distinct(StringComparer.Ordinal));
            var auditEvent = AuditEventBuilders.SecurityEvent(
                correlationId: Guid.NewGuid().ToString("N"),
                threatType: $"ToolResult:{toolName}",
                description: $"{sanitized.Threats.Count} injection pattern(s) in the result of '{toolName}', " +
                             $"{(sanitized.Blocked ? "withheld from the model" : "passed to the model tagged as data")}: {patterns}",
                severity: AuditSeverity.Warning,
                agentRole: caller.AgentRole) with
            {
                Outcome = sanitized.Blocked ? AuditOutcome.Blocked : AuditOutcome.Warning,
                CrewId = caller.CrewId,
                TaskId = caller.TaskId,
            };
            await _auditLogger.LogAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditFailed(ex, toolName);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool result of '{ToolName}' for agent '{AgentRole}' carries {Count} injection pattern(s); {Treatment}")]
    private partial void LogResultThreats(string toolName, string agentRole, int count, string treatment);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write the audit event of tool '{ToolName}'")]
    private partial void LogAuditFailed(Exception ex, string toolName);
}

/// <summary>
/// The optional collaborators of a <see cref="ToolInvocationPipeline"/> beyond its guardian, sanitizer, audit and gate.
/// </summary>
public sealed class ToolInvocationPipelineOptions
{
    /// <summary>Gets the channel where an <c>Ask</c> verdict of the gate is put to an operator; without it an <c>Ask</c> is a refusal.</summary>
    public IToolApprovalChannel? ApprovalChannel { get; init; }

    /// <summary>Gets the mode of a caller that declares none.</summary>
    public Orkeon.Domain.Security.PermissionMode DefaultMode { get; init; } = Orkeon.Domain.Security.PermissionMode.Default;

    /// <summary>Gets the ambient run: a tool that left it waiting for a human ends the agent's loop with the suspension (FWK-23).</summary>
    public Orkeon.Application.Interfaces.Services.IRunScope? RunScope { get; init; }
}
