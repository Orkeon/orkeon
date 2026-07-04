using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Coverage for <see cref="CircuitBreakerPolicyFactory"/>: preset resolution,
/// crew/task override merging, FSM creation and guard-context extraction.
/// </summary>
public sealed class CovSecurity_CircuitBreakerPolicyFactoryTests
{
    [Fact]
    public void Resolve_ShouldReturnStrict_WhenNothingConfigured()
    {
        var policy = CircuitBreakerPolicyFactory.Resolve(null, null);

        Assert.Equal(CircuitBreakerPolicy.Strict, policy);
    }

    [Theory]
    [InlineData("strict", 50)]
    [InlineData("permissive", 1000)]
    [InlineData("default", 100)]
    public void Resolve_ShouldHonorPreset(string preset, int expectedMaxTransitions)
    {
        var cfg = new CircuitBreakerConfig { Preset = preset };

        var policy = CircuitBreakerPolicyFactory.Resolve(cfg, null);

        Assert.Equal(expectedMaxTransitions, policy.MaxTransitions);
    }

    [Fact]
    public void Resolve_ShouldFallBackToStrict_WhenUnknownPreset()
    {
        var cfg = new CircuitBreakerConfig { Preset = "bogus" };

        var policy = CircuitBreakerPolicyFactory.Resolve(cfg, null);

        Assert.Equal(CircuitBreakerPolicy.Strict.MaxTransitions, policy.MaxTransitions);
    }

    [Fact]
    public void Resolve_ShouldApplyCrewOverrides_OnTopOfPreset()
    {
        var crew = new CircuitBreakerConfig
        {
            Preset = "default",
            MaxTransitions = 7,
            StateTimeoutSeconds = 120,
            MaxStateVisits = 3,
            MaxTotalDurationSeconds = 600,
            UseDegradedMode = true
        };

        var policy = CircuitBreakerPolicyFactory.Resolve(crew, null);

        Assert.Equal(7, policy.MaxTransitions);
        Assert.Equal(TimeSpan.FromSeconds(120), policy.StateTimeout);
        Assert.Equal(3, policy.MaxStateVisits);
        Assert.Equal(TimeSpan.FromSeconds(600), policy.MaxTotalDuration);
        Assert.True(policy.UseDegradedMode);
    }

    [Fact]
    public void Resolve_ShouldLetTaskOverrideWin_OverCrewDefault()
    {
        var crew = new CircuitBreakerConfig { Preset = "default", MaxTransitions = 10 };
        var task = new CircuitBreakerConfig { MaxTransitions = 99 };

        var policy = CircuitBreakerPolicyFactory.Resolve(crew, task);

        Assert.Equal(99, policy.MaxTransitions);
    }

    [Fact]
    public void Resolve_ShouldUseTaskPreset_OverCrewPreset()
    {
        var crew = new CircuitBreakerConfig { Preset = "strict" };
        var task = new CircuitBreakerConfig { Preset = "permissive" };

        var policy = CircuitBreakerPolicyFactory.Resolve(crew, task);

        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxTransitions, policy.MaxTransitions);
    }

    [Fact]
    public void Resolve_ShouldKeepPresetValues_WhenOverrideFieldsNull()
    {
        // Override config present but all override fields null -> preset values retained.
        var task = new CircuitBreakerConfig { Preset = "permissive" };

        var policy = CircuitBreakerPolicyFactory.Resolve(null, task);

        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxStateVisits, policy.MaxStateVisits);
        Assert.Equal(CircuitBreakerPolicy.Permissive.StateTimeout, policy.StateTimeout);
    }

    [Fact]
    public void CreateTaskFsm_ShouldBuildMachine_InInitialState()
    {
        var fsm = CircuitBreakerPolicyFactory.CreateTaskFsm(
            new CircuitBreakerConfig { Preset = "default" }, null);

        Assert.NotNull(fsm);
        Assert.Equal(TaskExecutionState.Assigned, fsm.CurrentState);
    }

    [Fact]
    public void CreateTaskFsm_ShouldBuildMachine_WhenNoConfig()
    {
        var fsm = CircuitBreakerPolicyFactory.CreateTaskFsm(null, null);

        Assert.NotNull(fsm);
        Assert.Equal(TaskExecutionState.Assigned, fsm.CurrentState);
    }

    [Fact]
    public void CreateGuardContext_ShouldUseDefaults_WhenNoConfig()
    {
        var ctx = CircuitBreakerPolicyFactory.CreateGuardContext(null, null);

        Assert.Equal(3, ctx.MaxRetries);
        Assert.Equal(10, ctx.MaxToolCallsPerRound);
        Assert.Equal(3, ctx.MaxValidationRetries);
        Assert.True(ctx.IsToolRegistered);
    }

    [Fact]
    public void CreateGuardContext_ShouldPreferTaskOverride()
    {
        var crew = new CircuitBreakerConfig { MaxRetries = 5, MaxToolCallsPerRound = 20, MaxValidationRetries = 2 };
        var task = new CircuitBreakerConfig { MaxRetries = 1, MaxToolCallsPerRound = 4, MaxValidationRetries = 1 };

        var ctx = CircuitBreakerPolicyFactory.CreateGuardContext(crew, task);

        Assert.Equal(1, ctx.MaxRetries);
        Assert.Equal(4, ctx.MaxToolCallsPerRound);
        Assert.Equal(1, ctx.MaxValidationRetries);
    }

    [Fact]
    public void CreateGuardContext_ShouldUseCrewDefault_WhenNoTaskOverride()
    {
        var crew = new CircuitBreakerConfig { MaxRetries = 5, MaxToolCallsPerRound = 20, MaxValidationRetries = 2 };

        var ctx = CircuitBreakerPolicyFactory.CreateGuardContext(crew, null);

        Assert.Equal(5, ctx.MaxRetries);
        Assert.Equal(20, ctx.MaxToolCallsPerRound);
        Assert.Equal(2, ctx.MaxValidationRetries);
    }
}
