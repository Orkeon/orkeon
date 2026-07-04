using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Infrastructure.Agent;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Tests for AgentLifecycleManager and related kill switch functionality.
/// </summary>
public class AgentLifecycleManagerTests
{
    #region Test Helpers

    private static NullLogger<AgentLifecycleManager> CreateLogger()
        => NullLogger<AgentLifecycleManager>.Instance;

    private static AgentLifecycleManager CreateManager()
        => new(CreateLogger());

    private static AgentId NewAgentId()
        => AgentId.From(Guid.NewGuid());

    #endregion

    // --- Test 1 ---

    [Fact]
    public void Register_ValidAgent_IsRegistered()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();
        using var cts = new CancellationTokenSource();

        manager.Register(agentId, cts);

        Assert.True(manager.IsRegistered(agentId));
        Assert.Equal(AgentLifecycleState.Registered, manager.GetState(agentId));
    }

    // --- Test 2 ---

    [Fact]
    public void Kill_RegisteredAgent_CancelsToken()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();
        using var cts = new CancellationTokenSource();

        manager.Register(agentId, cts);
        manager.Kill(agentId, "test kill");

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(AgentLifecycleState.Killed, manager.GetState(agentId));
    }

    // --- Test 3 ---

    [Fact]
    public void Kill_UnregisteredAgent_IsNoOp()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();

        // Should not throw
        manager.Kill(agentId, "no-op kill");

        Assert.Equal(AgentLifecycleState.Unknown, manager.GetState(agentId));
    }

    // --- Test 4 ---

    [Fact]
    public void KillAll_MultipleAgents_CancelsAll()
    {
        var manager = CreateManager();
        var ids = Enumerable.Range(0, 3).Select(_ => NewAgentId()).ToList();
        var ctsList = ids.Select(_ => new CancellationTokenSource()).ToList();

        for (int i = 0; i < ids.Count; i++)
            manager.Register(ids[i], ctsList[i]);

        manager.KillAll("mass kill");

        foreach (var cts in ctsList)
            Assert.True(cts.IsCancellationRequested);

        foreach (var id in ids)
            Assert.Equal(AgentLifecycleState.Killed, manager.GetState(id));

        // Dispose
        ctsList.ForEach(c => c.Dispose());
    }

    // --- Test 5 ---

    [Fact]
    public async System.Threading.Tasks.Task StopGraceful_WaitsBeforeCancel()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();
        using var cts = new CancellationTokenSource();

        manager.Register(agentId, cts);

        // Use very short timeout so test finishes quickly
        var stopTask = manager.StopGracefulAsync(agentId, TimeSpan.FromMilliseconds(50));

        // State should be StopRequested quickly — poll deterministically (R5.6) instead of a fixed delay
        await Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync(
            () => manager.GetState(agentId) == AgentLifecycleState.StopRequested);
        Assert.Equal(AgentLifecycleState.StopRequested, manager.GetState(agentId));

        await stopTask;

        // After timeout elapses, agent should be killed
        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(AgentLifecycleState.Killed, manager.GetState(agentId));
    }

    // --- Test 6 ---

    [Fact]
    public void GetState_AfterKill_ReturnsKilled()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();
        using var cts = new CancellationTokenSource();

        manager.Register(agentId, cts);
        Assert.Equal(AgentLifecycleState.Registered, manager.GetState(agentId));

        manager.Kill(agentId, "state check");
        Assert.Equal(AgentLifecycleState.Killed, manager.GetState(agentId));
    }

    // --- Test 7 ---

    [Fact]
    public async System.Threading.Tasks.Task Agent_StopAsync_RaisesKilledEvent()
    {
        var agent = DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("tester"),
            Goal = AgentGoal.From("test the kill switch"),
        });

        using var cts = new CancellationTokenSource();
        agent.RegisterCancellation(cts);

        await agent.StopAsync("test reason");

        var events = agent.DomainEvents.OfType<AgentKilledEvent>().ToList();
        Assert.Single(events);
        Assert.Equal("test reason", events[0].Reason);
        Assert.Equal(agent.Id, events[0].AgentId);
    }

    // --- Test 8 ---

    [Fact]
    public async System.Threading.Tasks.Task Agent_StopAsync_WithoutRegistration_Throws()
    {
        var agent = DomainAgent.Create(new AgentCreateOptions
        {
            Role = AgentRole.From("tester"),
            Goal = AgentGoal.From("test the kill switch"),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.StopAsync("should throw"));
    }

    // --- Test 9 ---

    [Fact]
    public void GuardianPipeline_AutoKill_OnCriticalViolation()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();
        using var cts = new CancellationTokenSource();
        manager.Register(agentId, cts);

        var violation = new GuardViolation(
            "TestGuard",
            GuardPhase.Input,
            "Prompt injection detected",
            GuardThreatSeverity.High,
            DateTime.UtcNow);

        // Simulate GuardianPipeline calling TryAutoKill via Kill directly
        // (The pipeline method is private — test the manager behavior directly)
        bool hasCritical = new[] { violation }.Any(v =>
            v.Severity is GuardThreatSeverity.High or GuardThreatSeverity.Critical);

        Assert.True(hasCritical);

        if (hasCritical && manager.IsRegistered(agentId))
        {
            manager.Kill(agentId, $"Guardian auto-kill: {violation.Description}");
        }

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(AgentLifecycleState.Killed, manager.GetState(agentId));
    }

    // --- Test 10: IsRegistered returns false for unknown agent ---

    [Fact]
    public void IsRegistered_UnknownAgent_ReturnsFalse()
    {
        var manager = CreateManager();
        var agentId = NewAgentId();

        Assert.False(manager.IsRegistered(agentId));
        Assert.Equal(AgentLifecycleState.Unknown, manager.GetState(agentId));
    }
}
