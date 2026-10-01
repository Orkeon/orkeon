using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
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
    public const string DelegateWorkToolName = "delegate_work_to_coworker";

    /// <summary>The tool a coworker is asked a question through; checked by the delegation phase.</summary>
    public const string AskQuestionToolName = "ask_question_to_coworker";

    /// <summary>
    /// The roles of the agents that delegated synchronously, outermost first: a delegated
    /// task runs inside its delegator's tool call, so its own calls see the chain grow.
    /// </summary>
    private static readonly AsyncLocal<ImmutableList<string>?> s_delegationChain = new();

    private readonly IGuardianPipeline? _guardian;
    private readonly IToolResultSanitizer? _sanitizer;
    private readonly IAuditLogger? _auditLogger;
    private readonly ILogger _logger;

    /// <summary>A pipeline with no guardian, no sanitizer and no audit: the call and the truncation only.</summary>
    public static ToolInvocationPipeline Unguarded { get; } = new();

    /// <summary>Initializes a new instance of <see cref="ToolInvocationPipeline"/>.</summary>
    /// <param name="guardian">The guardian pipeline running the tool and delegation phases.</param>
    /// <param name="sanitizer">The sanitizer applied to every successful result.</param>
    /// <param name="auditLogger">The audit trail a <c>ToolExecution</c> event is written to per call.</param>
    /// <param name="logger">The logger.</param>
    public ToolInvocationPipeline(
        IGuardianPipeline? guardian = null,
        IToolResultSanitizer? sanitizer = null,
        IAuditLogger? auditLogger = null,
        ILogger<ToolInvocationPipeline>? logger = null)
    {
        _guardian = guardian;
        _sanitizer = sanitizer;
        _auditLogger = auditLogger;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

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
        var isDelegation = IsDelegationTool(tool.Name);
        var chain = s_delegationChain.Value ?? ImmutableList<string>.Empty;

        var verdict = await GuardAsync(invocation, caller, isDelegation, chain, cancellationToken).ConfigureAwait(false);
        if (verdict is { IsAllowed: false })
        {
            var text = $"Error: Blocked by Guardian ({(isDelegation ? GuardPhase.Delegation : GuardPhase.ToolExecution)}): {verdict.Reason}";
            await AuditCallAsync(tool.Name, caller, AuditOutcome.Blocked, 0, cancellationToken).ConfigureAwait(false);
            return new ToolInvocationResult { ConversationText = text, RawText = text, Blocked = true };
        }

        var stopwatch = Stopwatch.StartNew();
        ToolCallResponse response;
        try
        {
            // A synchronous delegation runs the coworker's task inside this call: its own
            // tool calls see this agent at the end of the chain.
            if (isDelegation)
                s_delegationChain.Value = chain.Add(caller.AgentRole);

            response = await tool.CallAsync(new ToolCallRequest(tool.Name, invocation.Arguments), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await AuditCallAsync(tool.Name, caller, AuditOutcome.Failure, stopwatch.ElapsedMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (isDelegation)
                s_delegationChain.Value = chain;
        }

        var rawText = response.Success
            ? ToolCallFormatting.FormatResult(response.Result)
            : $"Error: {response.Error}";

        // The one truncation rule (AgentDefaults.ResolveMaxToolResultLength), applied before the
        // sanitizer so that what is screened is exactly what the model reads.
        var conversationText = ConversationPolicy.TruncateToolResult(
            rawText, AgentDefaults.ResolveMaxToolResultLength(tool.Name));

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

    private async System.Threading.Tasks.Task<GuardResult?> GuardAsync(
        ToolInvocation invocation,
        ToolInvocationCaller caller,
        bool isDelegation,
        ImmutableList<string> chain,
        CancellationToken cancellationToken)
    {
        if (_guardian is null)
            return null;

        var context = new GuardContext
        {
            Phase = isDelegation ? GuardPhase.Delegation : GuardPhase.ToolExecution,
            AgentId = caller.AgentId,
            AgentRole = caller.AgentRole,
            CrewId = caller.CrewId ?? string.Empty,
            ToolName = invocation.Tool.Name,
            ToolArgs = invocation.Arguments,
            DelegationChain = chain,
            TargetAgentRole = isDelegation ? ReadCoworkerRole(invocation.Arguments) : null,
        };

        return await _guardian.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="toolName"/> hands work or a question to another agent.</summary>
    public static bool IsDelegationTool(string toolName) =>
        string.Equals(toolName, DelegateWorkToolName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolName, AskQuestionToolName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The coworker a delegation targets, whatever casing the model gave the argument
    /// (<c>coworker_role</c>, <c>coworkerRole</c>, <c>CoworkerRole</c>).
    /// </summary>
    private static string? ReadCoworkerRole(IReadOnlyDictionary<string, object?> arguments)
    {
        foreach (var (key, value) in arguments)
        {
            if (string.Equals(key.Replace("_", "", StringComparison.Ordinal), "coworkerrole", StringComparison.OrdinalIgnoreCase))
                return value?.ToString();
        }

        return null;
    }

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
