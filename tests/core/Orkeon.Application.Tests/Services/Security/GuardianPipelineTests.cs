using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.Common;
using Orkeon.Domain.Security;

namespace Orkeon.Application.Tests.Services.Security;

/// <summary>
/// SONAR-14 T2: pins the guardian pipeline — phase gating by policy, first-block wins,
/// warning aggregation, the fail-safe block on a throwing guardian, audit logging
/// (including a failing audit sink) and the AutoKill escalation rules.
/// </summary>
public class GuardianPipelineTests
{
    private sealed class ScriptedGuardian : IGuardian
    {
        private readonly Func<GuardContext, GuardResult> _check;

        public ScriptedGuardian(Func<GuardContext, GuardResult> check) => _check = check;

        public static ScriptedGuardian Allowing() => new(_ => GuardResult.Allow());

        public static ScriptedGuardian Throwing(Exception exception) => new(_ => throw exception);

        public int Checks { get; private set; }

        public System.Threading.Tasks.Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
        {
            Checks++;
            return System.Threading.Tasks.Task.FromResult(_check(context));
        }
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEvent> Events { get; } = [];

        public Exception? ThrowOnLog { get; set; }

        public System.Threading.Tasks.Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            if (ThrowOnLog is not null) throw ThrowOnLog;
            Events.Add(auditEvent);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public IAuditScope BeginScope(string correlationId, string? crewId = null) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private sealed class RecordingLifecycleManager : IAgentLifecycleManager
    {
        public HashSet<AgentId> Registered { get; } = [];

        public List<(AgentId AgentId, string Reason)> Kills { get; } = [];

        public void Register(AgentId agentId, CancellationTokenSource cts) => Registered.Add(agentId);

        public System.Threading.Tasks.Task StopGracefulAsync(AgentId agentId, TimeSpan? timeout = null) =>
            System.Threading.Tasks.Task.CompletedTask;

        public void Kill(AgentId agentId, string reason) => Kills.Add((agentId, reason));

        public void KillAll(string reason)
        {
        }

        public AgentLifecycleState GetState(AgentId agentId) => AgentLifecycleState.Running;

        public bool IsRegistered(AgentId agentId) => Registered.Contains(agentId);
    }

    private static GuardViolation Violation(GuardThreatSeverity severity) =>
        new("TestGuard", GuardPhase.Input, "violation", severity, DateTime.UtcNow);

    private static GuardianPipeline BuildPipeline(
        GuardianPolicy? policy = null,
        IAuditLogger? audit = null,
        IAgentLifecycleManager? lifecycle = null) =>
        new(new GuardianPolicyEngine(policy ?? new GuardianPolicy()),
            new TestLogger<GuardianPipeline>(),
            audit,
            lifecycle);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private static GuardContext InputContext(string agentId = "") =>
        new() { Phase = GuardPhase.Input, CrewId = "crew-1", AgentId = agentId };

    [Fact]
    public async System.Threading.Tasks.Task Allows_WhenThePhaseIsDisabledByPolicy_WithoutCallingGuards()
    {
        var guard = ScriptedGuardian.Allowing();
        var pipeline = BuildPipeline(new GuardianPolicy { InputGuardEnabled = false });
        pipeline.AddGuard(GuardPhase.Input, guard);

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Allow, result.Action);
        Assert.Equal(0, guard.Checks);
    }

    [Fact]
    public async System.Threading.Tasks.Task Allows_WhenNoGuardIsRegisteredForThePhase()
    {
        var pipeline = BuildPipeline();

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async System.Threading.Tasks.Task TheFirstBlock_StopsThePipeline_AndIsAudited()
    {
        var audit = new RecordingAuditLogger();
        var blocking = new ScriptedGuardian(_ => GuardResult.Block("forbidden", [Violation(GuardThreatSeverity.Medium)]));
        var never = ScriptedGuardian.Allowing();
        var pipeline = BuildPipeline(audit: audit);
        pipeline.AddGuard(GuardPhase.Input, blocking);
        pipeline.AddGuard(GuardPhase.Input, never);

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Block, result.Action);
        Assert.Equal("forbidden", result.Reason);
        Assert.Equal(0, never.Checks);
        Assert.Single(audit.Events);
    }

    [Fact]
    public async System.Threading.Tasks.Task AggregatesWarnings_AcrossGuards()
    {
        var warn1 = new ScriptedGuardian(_ => GuardResult.Warn("w1", [Violation(GuardThreatSeverity.Low)]));
        var warn2 = new ScriptedGuardian(_ => GuardResult.Warn("w2", [Violation(GuardThreatSeverity.Low)]));
        var pipeline = BuildPipeline(audit: new RecordingAuditLogger());
        pipeline.AddGuard(GuardPhase.Input, warn1);
        pipeline.AddGuard(GuardPhase.Input, warn2);

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Warn, result.Action);
        Assert.Equal(2, result.Violations.Count);
        Assert.Contains("2 warning(s)", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task AllGuardsAllowing_YieldsAllow()
    {
        var pipeline = BuildPipeline();
        pipeline.AddGuard(GuardPhase.Input, ScriptedGuardian.Allowing());
        pipeline.AddGuard(GuardPhase.Input, ScriptedGuardian.Allowing());

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Allow, result.Action);
    }

    [Fact]
    public async System.Threading.Tasks.Task AThrowingGuardian_BlocksFailSafe()
    {
        var pipeline = BuildPipeline(audit: new RecordingAuditLogger());
        pipeline.AddGuard(GuardPhase.Input, ScriptedGuardian.Throwing(new InvalidOperationException("guard bug")));

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Block, result.Action);
        Assert.Contains("guard bug", result.Reason, StringComparison.Ordinal);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(GuardThreatSeverity.High, violation.Severity);
    }

    [Fact]
    public async System.Threading.Tasks.Task Cancellation_PropagatesInsteadOfBlocking()
    {
        var pipeline = BuildPipeline();
        pipeline.AddGuard(GuardPhase.Input, ScriptedGuardian.Throwing(new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.ExecuteAsync(InputContext(), CancellationToken.None));
    }

    [Fact]
    public async System.Threading.Tasks.Task AFailingAuditSink_NeverMasksTheDecision()
    {
        var audit = new RecordingAuditLogger { ThrowOnLog = new InvalidOperationException("sink down") };
        var pipeline = BuildPipeline(audit: audit);
        pipeline.AddGuard(GuardPhase.Input, new ScriptedGuardian(_ =>
            GuardResult.Block("still blocked", [Violation(GuardThreatSeverity.Low)])));

        var result = await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Equal(GuardAction.Block, result.Action);
        Assert.Equal("still blocked", result.Reason);
    }

    // ── AutoKill escalation ───────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task AutoKill_KillsARegisteredAgent_OnACriticalBlock()
    {
        var lifecycle = new RecordingLifecycleManager();
        var agentGuid = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        lifecycle.Register(AgentId.From(agentGuid), cts);

        var pipeline = BuildPipeline(lifecycle: lifecycle);
        pipeline.AutoKillOnCritical = true;
        pipeline.AddGuard(GuardPhase.Input, new ScriptedGuardian(_ =>
            GuardResult.Block("critical breach", [Violation(GuardThreatSeverity.Critical)])));

        await pipeline.ExecuteAsync(InputContext(agentGuid.ToString()), TestContext.Current.CancellationToken);

        var kill = Assert.Single(lifecycle.Kills);
        Assert.Equal(AgentId.From(agentGuid), kill.AgentId);
        Assert.Contains("critical breach", kill.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task AutoKill_IsSkipped_WhenTheFlagIsOff()
    {
        var lifecycle = new RecordingLifecycleManager();
        var agentGuid = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        lifecycle.Register(AgentId.From(agentGuid), cts);

        var pipeline = BuildPipeline(lifecycle: lifecycle);
        pipeline.AddGuard(GuardPhase.Input, new ScriptedGuardian(_ =>
            GuardResult.Block("critical breach", [Violation(GuardThreatSeverity.Critical)])));

        await pipeline.ExecuteAsync(InputContext(agentGuid.ToString()), TestContext.Current.CancellationToken);

        Assert.Empty(lifecycle.Kills);
    }

    [Fact]
    public async System.Threading.Tasks.Task AutoKill_IsSkipped_ForLowSeverityBlocks()
    {
        var lifecycle = new RecordingLifecycleManager();
        var agentGuid = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        lifecycle.Register(AgentId.From(agentGuid), cts);

        var pipeline = BuildPipeline(lifecycle: lifecycle);
        pipeline.AutoKillOnCritical = true;
        pipeline.AddGuard(GuardPhase.Input, new ScriptedGuardian(_ =>
            GuardResult.Block("mild", [Violation(GuardThreatSeverity.Low)])));

        await pipeline.ExecuteAsync(InputContext(agentGuid.ToString()), TestContext.Current.CancellationToken);

        Assert.Empty(lifecycle.Kills);
    }

    [Fact]
    public async System.Threading.Tasks.Task AutoKill_IsSkipped_ForAnUnregisteredOrUnparsableAgent()
    {
        var lifecycle = new RecordingLifecycleManager();
        var pipeline = BuildPipeline(lifecycle: lifecycle);
        pipeline.AutoKillOnCritical = true;
        pipeline.AddGuard(GuardPhase.Input, new ScriptedGuardian(_ =>
            GuardResult.Block("critical", [Violation(GuardThreatSeverity.Critical)])));

        // Unregistered but parsable agent id.
        await pipeline.ExecuteAsync(InputContext(Guid.NewGuid().ToString()), TestContext.Current.CancellationToken);
        // Unparsable agent id.
        await pipeline.ExecuteAsync(InputContext("not-a-guid"), TestContext.Current.CancellationToken);
        // Empty agent id.
        await pipeline.ExecuteAsync(InputContext(), TestContext.Current.CancellationToken);

        Assert.Empty(lifecycle.Kills);
    }
}
