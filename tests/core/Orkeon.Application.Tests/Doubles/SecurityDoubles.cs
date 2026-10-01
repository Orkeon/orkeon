using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IGuardianPipeline"/>: records every context it is asked about and
/// answers with the verdict the test scripts (allow by default).
/// </summary>
internal sealed class FakeGuardianPipeline : IGuardianPipeline
{
    private readonly Func<GuardContext, GuardResult> _verdict;

    public FakeGuardianPipeline(Func<GuardContext, GuardResult>? verdict = null)
        => _verdict = verdict ?? (_ => GuardResult.Allow());

    /// <summary>Blocks every call to <paramref name="toolName"/>, allows everything else.</summary>
    public static FakeGuardianPipeline BlockingTool(string toolName) => new(context =>
        string.Equals(context.ToolName, toolName, StringComparison.Ordinal)
            ? GuardResult.Block($"'{toolName}' refused by the test policy",
                [new GuardViolation("FakeGuard", context.Phase, "refused", GuardThreatSeverity.High, DateTime.UtcNow)])
            : GuardResult.Allow());

    /// <summary>Blocks the input phase, allows everything else.</summary>
    public static FakeGuardianPipeline BlockingInput() => new(context =>
        context.Phase == GuardPhase.Input
            ? GuardResult.Block("prompt injection suspected",
                [new GuardViolation("FakeGuard", GuardPhase.Input, "injection", GuardThreatSeverity.High, DateTime.UtcNow)])
            : GuardResult.Allow());

    public List<GuardContext> Contexts { get; } = [];

    public System.Threading.Tasks.Task<GuardResult> ExecuteAsync(GuardContext context, CancellationToken ct = default)
    {
        Contexts.Add(context);
        return System.Threading.Tasks.Task.FromResult(_verdict(context));
    }
}

/// <summary>
/// Hand-written <see cref="IToolResultSanitizer"/>: tags every result between
/// <c>[DATA]</c> markers and reports one High threat when the result asks the model to
/// ignore its previous instructions.
/// </summary>
internal sealed class FakeToolResultSanitizer : IToolResultSanitizer
{
    public List<(string Tool, string Result, string Agent)> Calls { get; } = [];

    public ToolResultSanitization Sanitize(string toolName, string result, string agentRole)
    {
        Calls.Add((toolName, result, agentRole));
        IReadOnlyList<ThreatDetection> threats = result.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase)
            ? [new ThreatDetection(ThreatType.PromptInjection, "Ignore previous instructions", "ignore previous instructions", 0, ThreatSeverity.High)]
            : [];
        return new ToolResultSanitization { Text = $"[DATA]{result}[/DATA]", Threats = threats };
    }
}

/// <summary>Hand-written <see cref="IAuditLogger"/> recording every event it is given.</summary>
internal sealed class MockAuditLogger : IAuditLogger
{
    public List<AuditEvent> Events { get; } = [];

    public System.Threading.Tasks.Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        Events.Add(auditEvent);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public IAuditScope BeginScope(string correlationId, string? crewId = null) => throw new NotSupportedException();

    public System.Threading.Tasks.Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
}

/// <summary>
/// Hand-written <see cref="IToolInvocationPipeline"/> that records every invocation and runs
/// the real <c>ToolInvocationPipeline</c> behind it — the per-loop tests prove a loop goes
/// through the port, and see what it feeds back.
/// </summary>
internal sealed class RecordingToolInvocationPipeline : IToolInvocationPipeline
{
    private readonly IToolInvocationPipeline _inner;

    public RecordingToolInvocationPipeline(IToolInvocationPipeline inner) => _inner = inner;

    public List<ToolInvocation> Invocations { get; } = [];

    public List<ToolInvocationResult> Results { get; } = [];

    public async System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        Invocations.Add(invocation);
        var result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        Results.Add(result);
        return result;
    }
}
